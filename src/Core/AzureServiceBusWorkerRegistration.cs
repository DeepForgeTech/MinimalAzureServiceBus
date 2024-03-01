using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;

namespace MinimalAzureServiceBus.Core
{
    public class AzureServiceBusWorkerRegistration
    {
        protected Dictionary<(string Name, string Type), Func<IServiceScopeFactory, Func<ProcessMessageEventArgs, Task>>> _handlerRegistrations = new Dictionary<(string Name, string Type), Func<IServiceScopeFactory, Func<ProcessMessageEventArgs, Task>>>();

        internal AzureServiceBusWorkerRegistration(string serviceBusConnectionString, string appName)
        {
            _serviceBusConnectionString = serviceBusConnectionString;
            _appName = appName;
        }

        protected readonly string _serviceBusConnectionString;
        protected readonly string _appName;

        public AzureServiceBusWorkerRegistration ProcessQueue<TMessage>(string queueName, Func<TMessage, Task> handler) where TMessage : class
        {
            _handlerRegistrations.Add((queueName, "Queue"), MessageHandler(handler));

            return this;
        }

        public AzureServiceBusWorkerRegistration ProcessQueue<TMessage, TService>(string queueName, Func<TMessage, TService, Task> handler) where TMessage : class where TService : notnull
        {
            _handlerRegistrations.Add((queueName, "Queue"), MessageHandler(handler));

            return this;
        }

        public AzureServiceBusWorkerRegistration ProcessQueue<TMessage, TService1, TService2>(string queueName, Func<TMessage, TService1, TService2, Task> handler)
            where TMessage : class
            where TService1 : notnull
            where TService2 : notnull
        {
            _handlerRegistrations.Add((queueName, "Queue"), MessageHandler(handler));

            return this;
        }

        public AzureServiceBusWorkerRegistration SubscribeTopic<TMessage>(string topicName, Func<TMessage, Task> handler) where TMessage : class
        {
            _handlerRegistrations.Add((topicName, "Topic"), MessageHandler(handler));

            return this;
        }

        public AzureServiceBusWorkerRegistration SubscribeTopic<TMessage, TService>(string topicName, Func<TMessage, TService, Task> handler) where TMessage : class where TService : notnull
        {
            _handlerRegistrations.Add((topicName, "Topic"), MessageHandler(handler));

            return this;
        }

        private Func<IServiceScopeFactory, Func<ProcessMessageEventArgs, Task>> MessageHandler<TMessage>(Func<TMessage, Task> handler)
        {
            return serviceProvider => async arg =>
            {
                var body = arg.Message.Body;
                var message = JsonSerializer.Deserialize<TMessage>(body);

                await handler(message);
            };
        }
        private Func<IServiceScopeFactory, Func<ProcessMessageEventArgs, Task>> MessageHandler<TMessage, TService>(Func<TMessage, TService, Task> handler) where TService : notnull
        {
            return serviceScopeFactory => async arg =>
            {
                await using var scope = serviceScopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<TService>();

                var body = arg.Message.Body.ToString();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var message = JsonSerializer.Deserialize<TMessage>(body, options);

                await handler(message, service);
            };
        }

        private Func<IServiceScopeFactory, Func<ProcessMessageEventArgs, Task>> MessageHandler<TMessage, TService1, TService2>(Func<TMessage, TService1, TService2, Task> handler) where TService1 : notnull where TService2 : notnull
        {
            return serviceScopeFactory => async arg =>
            {
                await using var scope = serviceScopeFactory.CreateAsyncScope();
                var service1 = scope.ServiceProvider.GetRequiredService<TService1>();
                var service2 = scope.ServiceProvider.GetRequiredService<TService2>();

                var body = arg.Message.Body;
                var message = JsonSerializer.Deserialize<TMessage>(body);

                await handler(message, service1, service2);
            };
        }
    }
}