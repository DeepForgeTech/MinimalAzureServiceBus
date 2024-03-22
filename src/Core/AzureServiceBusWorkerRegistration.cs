using System;
using System.Collections.Generic;
using MinimalAzureServiceBus.Core.Models;

namespace MinimalAzureServiceBus.Core
{
    public enum ServiceBusType
    {
        Queue,
        Topic
    }

    public class AzureServiceBusWorkerRegistration
    {
        protected Dictionary<(string Name, ServiceBusType RegistrationType), (Delegate, ProcessingConfiguration)> _delegateHandlerRegistrations = new Dictionary<(string Name, ServiceBusType RegistrationType), (Delegate, ProcessingConfiguration)>();

        internal AzureServiceBusWorkerRegistration(string serviceBusConnectionString, string appName)
        {
            _serviceBusConnectionString = serviceBusConnectionString;
            _appName = appName;
        }

        protected readonly string _serviceBusConnectionString;
        protected readonly string _appName;

        protected ErrorHandlingConfiguration _errorHandlingConfiguration = new ErrorHandlingConfiguration();
        protected ProcessingConfiguration _defaultProcessingConfiguration = new ProcessingConfiguration();

        protected static ProcessingConfiguration ConfigureProcessing(
            ProcessingConfiguration initialConfig,
            Action<ProcessingConfiguration>? configurationAction = null)
        {
            // Clone the initial configuration to ensure that the original instance is not modified.
            var newConfig = new ProcessingConfiguration
            {
                PrefetchCount = initialConfig.PrefetchCount,
                MaxConcurrentCalls = initialConfig.MaxConcurrentCalls,
                LockDuration = initialConfig.LockDuration,
                MaxAutoLockRenewalDuration = initialConfig.MaxAutoLockRenewalDuration
            };

            // Apply the configuration action if it's provided.
            configurationAction?.Invoke(newConfig);

            return newConfig;
        }

        private Func<string, Delegate, Action<ProcessingConfiguration>?, AzureServiceBusWorkerRegistration> AddRegistration(ServiceBusType registrationType) =>
            (name, handler, createConfiguration) =>
            {
                _delegateHandlerRegistrations.Add((name, registrationType), (handler, ConfigureProcessing(_defaultProcessingConfiguration, createConfiguration)));

                return this;
            };

        public AzureServiceBusWorkerRegistration ProcessQueue(string queueName, Delegate handler, Action<ProcessingConfiguration>? createConfiguration = null) =>
            AddRegistration(ServiceBusType.Queue)(queueName, handler, createConfiguration);
        public AzureServiceBusWorkerRegistration SubscribeTopic(string topicName, Delegate handler, Action<ProcessingConfiguration>? createConfiguration = null) =>
            AddRegistration(ServiceBusType.Topic)(topicName, handler, createConfiguration);

        public AzureServiceBusWorkerRegistration WithDefaultConfiguration(Action<ProcessingConfiguration> configure)
        {
            var configuration = new ProcessingConfiguration();

            configure(configuration);

            return WithDefaultConfiguration(configuration);
        }

        public AzureServiceBusWorkerRegistration WithDefaultConfiguration(ProcessingConfiguration processingConfiguration)
        {
            _defaultProcessingConfiguration = processingConfiguration;

            return this;
        }

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
    }
}