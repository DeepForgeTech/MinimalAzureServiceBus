using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;

namespace MinimalAzureServiceBus.Core
{
    public delegate AzureServiceBusWorkerRegistration AzureServiceBusWorkerRegistrationDelegate(
        string queueName,
        Func<AsyncServiceScope, string, Task> handlerFactoryFunc);

    public class AzureServiceBusWorkerRegistration
    {
        protected Dictionary<(string Name, string Type), Func<AsyncServiceScope, string, Task>> _handlerRegistrations = new Dictionary<(string Name, string Type), Func<AsyncServiceScope, string, Task>>();

        private readonly AzureServiceBusWorkerRegistrationDelegate AddQueueRegistration;
        private readonly AzureServiceBusWorkerRegistrationDelegate AddTopicRegistration;

        internal AzureServiceBusWorkerRegistration(string serviceBusConnectionString, string appName)
        {
            _serviceBusConnectionString = serviceBusConnectionString;
            _appName = appName;

            AddQueueRegistration = AddRegistration("Queue");
            AddTopicRegistration = AddRegistration("Topic");
        }

        protected readonly string _serviceBusConnectionString;
        protected readonly string _appName;

        protected ErrorHandlingConfiguration? _errorHandlingConfiguration;

        private AzureServiceBusWorkerRegistrationDelegate AddRegistration(string type) =>
            (name, handlerFactory) =>
            {
                _handlerRegistrations.Add((name, type), handlerFactory);

                return this;
            };


        public AzureServiceBusWorkerRegistration ProcessQueue<TMessage>(string queueName, Func<TMessage, Task> handler) where TMessage : class => 
            AddQueueRegistration(queueName, MessageHandler(handler));

        public AzureServiceBusWorkerRegistration ProcessQueue<TMessage, TService>(string queueName, Func<TMessage, TService, Task> handler) where TMessage : class where TService : notnull =>
            AddQueueRegistration(queueName, MessageHandler(handler));

        public AzureServiceBusWorkerRegistration ProcessQueue<TMessage, TService1, TService2>(string queueName, Func<TMessage, TService1, TService2, Task> handler)
            where TMessage : class
            where TService1 : notnull
            where TService2 : notnull =>
            AddQueueRegistration(queueName, MessageHandler(handler));

        public AzureServiceBusWorkerRegistration SubscribeTopic<TMessage>(string topicName, Func<TMessage, Task> handler) where TMessage : class
            => AddTopicRegistration(topicName, MessageHandler(handler));

        public AzureServiceBusWorkerRegistration SubscribeTopic<TMessage, TService>(string topicName, Func<TMessage, TService, Task> handler) where TMessage : class where TService : notnull
            => AddTopicRegistration(topicName, MessageHandler(handler));

        private static Func<AsyncServiceScope, string, Task> MessageHandler<TMessage>(Func<TMessage, Task> handler) =>
            async (scope, body) =>
            {
                var message = JsonSerializer.Deserialize<TMessage>(body);

                await handler(message);
            };

        private static Func<AsyncServiceScope, string, Task> MessageHandler<TMessage, TService1, TService2>(Func<TMessage, TService1, TService2, Task> handler) where TService1 : notnull where TService2 : notnull =>
            async (scope, body) =>
            {
                var service1 = scope.ServiceProvider.GetRequiredService<TService1>();
                var service2 = scope.ServiceProvider.GetRequiredService<TService2>();

                var message = JsonSerializer.Deserialize<TMessage>(body);

                await handler(message, service1, service2);
            };

        private static Func<AsyncServiceScope, string, Task> MessageHandler<TMessage, TService>(Func<TMessage, TService, Task> handler) where TService : notnull =>
            async (scope, body) =>
            {
                var service = scope.ServiceProvider.GetRequiredService<TService>();
                var message = JsonSerializer.Deserialize<TMessage>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                await handler(message, service);
            };

        /// <summary>
        /// Configures the behaviour for handling errors and retries. The default value for errorQueueName is $"{appName}-error"
        /// </summary>
        public AzureServiceBusWorkerRegistration EnableErrorHandling(string? errorQueueName = null)
        {
            _errorHandlingConfiguration = new ErrorHandlingConfiguration
            {
                ErrorQueueName = $"{_appName}-error"
            };

            return this;
        }

        /// <summary>
        /// Configures the behaviour for handling errors and retries. The default value for errorQueueName is $"{appName}-error"
        /// </summary>
        public AzureServiceBusWorkerRegistration EnableErrorHandling(ErrorHandlingConfiguration exceptionConfiguration)
        {
            _errorHandlingConfiguration = exceptionConfiguration;

            return this;
        }

        /// <summary>
        /// Configures the behaviour for handling errors and retries. The default value for errorQueueName is $"{appName}-error"
        /// </summary>
        public AzureServiceBusWorkerRegistration EnableErrorHandling(Action<ErrorHandlingConfiguration> configure)
        {
            _errorHandlingConfiguration = new ErrorHandlingConfiguration();

            configure(_errorHandlingConfiguration);

            return this;
        }
    }

    public class ErrorHandlingConfiguration
    {
        public string? ErrorQueueName { get; set; }
        public bool SendUnhandledExceptionsToErrorQueue { get; set; } = false;
        public int MaxAllowedRetries { get; set; } = 11;
    }
}