using System;
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
    internal class SenderInterceptor : IInterceptor, IDisposable
    {
        private readonly MessageSender _messageSender;

        internal SenderInterceptor(string connectionString, ILoggerFactory loggerFactory) => 
            _messageSender = new MessageSender(connectionString, loggerFactory.CreateLogger<MessageSender>());

        public void Intercept(IInvocation invocation)
        {
            if (invocation.Method.ReturnType != typeof(Task) || (invocation.Method.ReturnType.IsGenericType && invocation.Method.ReturnType.GetGenericTypeDefinition() != typeof(Task<>)))
                throw new InvalidOperationException("Only methods that return Task are supported");

            if (invocation.Arguments.Length > 1)
                throw new InvalidOperationException("Only one argument is allowed and that argument will be used for the body of the message");

            var methodName = invocation.Method.Name;
            var messageType = ServiceBusType.Unknown;
            var name = string.Empty;

            if (invocation.Method.GetCustomAttribute(inherit: true, attributeType:typeof(MessagingAttribute)) is MessagingAttribute messagingAttribute)
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

            var parameterType = invocation.Method.GetParameters().First().ParameterType;
            var body = JsonSerializer.Serialize(invocation.Arguments[0], parameterType);
            var message = new ServiceBusMessage { Body = BinaryData.FromString(body) };
            var dispatch = messageType switch
            {
                ServiceBusType.Queue => _messageSender.SendAsync(name, message),
                ServiceBusType.Topic => _messageSender.PublishAsync(name, message),
                _ => Task.CompletedTask
            };

            invocation.ReturnValue = dispatch;
        }

        public void Dispose()
        {
            // TODO release managed resources here
        }
    }
}