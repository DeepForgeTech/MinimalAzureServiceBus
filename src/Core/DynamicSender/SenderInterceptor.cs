using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Castle.DynamicProxy;
using Microsoft.Extensions.Logging;
using MinimalAzureServiceBus.Core.Models;

namespace MinimalAzureServiceBus.Core.DynamicSender
{
    public static class MessageSenderHelper
    {
        public static ConcurrentDictionary<string, (ServiceBusType Type, string Name)> _cache = new ConcurrentDictionary<string, (ServiceBusType Type, string Name)>();

        public static (ServiceBusType Type, string Name) Extract(MethodInfo methodInfo, ParameterInfo[] parameters)
        {
            var methodName = methodInfo.Name;
            var key = $"{methodInfo.DeclaringType?.FullName}.{methodName}";

            if (_cache.TryGetValue(key, out var extract))
                return extract;

            if (methodInfo.ReturnType != typeof(Task) || (methodInfo.ReturnType.IsGenericType && methodInfo.ReturnType.GetGenericTypeDefinition() != typeof(Task<>)))
                throw new InvalidOperationException("Only methods that return Task are supported");
            
            if (parameters.Length > 1)
                throw new InvalidOperationException("Only one argument is allowed and that argument will be used for the body of the message");

            var messageType = ServiceBusType.Unknown;
            var name = string.Empty;

            if (methodInfo.GetCustomAttribute(inherit: true, attributeType: typeof(MessagingAttribute)) is MessagingAttribute messagingAttribute)
            {
                name = string.IsNullOrEmpty(messagingAttribute.Name) ? methodName : messagingAttribute.Name;
                messageType = messagingAttribute switch
                {
                    QueueAttribute _ => ServiceBusType.Queue,
                    TopicAttribute _ => ServiceBusType.Topic,
                    _ => messageType
                };
            }
            else
            {
                if (methodName.StartsWith("Send", StringComparison.OrdinalIgnoreCase))
                    (messageType, name) = (ServiceBusType.Queue, methodName[4..]);
                else if (methodName.StartsWith("Publish"))
                    (messageType, name) = (ServiceBusType.Topic, methodName[7..]);
            }

            _cache.TryAdd(key, (messageType, name));

            return (messageType, name);
        }
    }

    internal class SenderInterceptor : IInterceptor, IDisposable
    {
        private readonly MessageSender _messageSender;

        internal SenderInterceptor(string connectionString, ILoggerFactory loggerFactory) =>
            _messageSender = new MessageSender(connectionString, loggerFactory.CreateLogger<MessageSender>());

        public void Dispose()
        {
            // TODO release managed resources here
        }

        public void Intercept(IInvocation invocation)
        {
            var parameters = invocation.Method.GetParameters();
            var (messageType, name) = MessageSenderHelper.Extract(invocation.Method, parameters);
            var parameterType = parameters.First().ParameterType;
            var body = JsonSerializer.Serialize(invocation.Arguments[0], parameterType);
            var message = new ServiceBusMessage {Body = BinaryData.FromString(body)};
            var dispatch = messageType switch
            {
                ServiceBusType.Queue => _messageSender.SendAsync(name, message),
                ServiceBusType.Topic => _messageSender.PublishAsync(name, message),
                _ => Task.CompletedTask
            };

            invocation.ReturnValue = dispatch;
        }
    }
}