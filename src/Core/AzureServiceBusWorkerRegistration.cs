using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace MinimalAzureServiceBus.Core
{
    public delegate AzureServiceBusWorkerRegistration AzureServiceBusWorkerRegistrationDelegate(
        string queueName,
        Func<AsyncServiceScope, string, Task> handlerFactoryFunc);

    public enum ServiceBusType
    {
        Queue,
        Topic
    }

    public class AzureServiceBusWorkerRegistration
    {
        protected Dictionary<(string Name, ServiceBusType RegistrationType), Delegate> _delegateHandlerRegistrations = new Dictionary<(string Name, ServiceBusType RegistrationType), Delegate>();

        internal AzureServiceBusWorkerRegistration(string serviceBusConnectionString, string appName)
        {
            _serviceBusConnectionString = serviceBusConnectionString;
            _appName = appName;
        }

        protected readonly string _serviceBusConnectionString;
        protected readonly string _appName;

        protected ErrorHandlingConfiguration? _errorHandlingConfiguration;

        private Func<string, Delegate, AzureServiceBusWorkerRegistration> AddRegistration(ServiceBusType registrationType) =>
            (name, handler) =>
            {
                _delegateHandlerRegistrations.Add((name, registrationType), handler);

                return this;
            };

        public AzureServiceBusWorkerRegistration ProcessQueue(string queueName, Delegate handler) =>
            AddRegistration(ServiceBusType.Queue)(queueName, handler);
        public AzureServiceBusWorkerRegistration SubscribeTopic(string topicName, Delegate handler) =>
            AddRegistration(ServiceBusType.Topic)(topicName, handler);

        /// <summary>
        /// Configures the behaviour for handling errors and retries. The default value for errorQueueName is $"{appName}-error"
        /// </summary>
        public AzureServiceBusWorkerRegistration EnableErrorHandling(string? errorQueueName = null)
        {
            _errorHandlingConfiguration = new ErrorHandlingConfiguration
            {
                ErrorQueueName = errorQueueName ?? $"{_appName}-error"
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