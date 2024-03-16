using System;
using System.Collections.Generic;

namespace MinimalAzureServiceBus.Core
{
    public class AzureServiceBusWorkerRegistrationDetail : AzureServiceBusWorkerRegistration
    {
        public AzureServiceBusWorkerRegistrationDetail(string serviceBusConnectionString, string appName) : base(serviceBusConnectionString, appName)
        {
        }

        internal ErrorHandlingConfiguration ErrorHandlingConfiguration => _errorHandlingConfiguration;
        internal string ServiceBusConnectionString => _serviceBusConnectionString;
        internal string AppName => _appName;
        internal Dictionary<(string Name, ServiceBusType RegistrationType), Delegate> DelegateHandlerRegistrations => _delegateHandlerRegistrations;
        public RetryConfiguration RetryConfiguration { get; set; } = new RetryConfiguration { MaxRetries = 10, RetryStrategy = RetryStrategy.Exponential };
    }

    public class RetryConfiguration
    {
        public int MaxRetries { get; set; }
        public TimeSpan Delay { get; set; }
        public RetryStrategy RetryStrategy { get; set; }
    }

    public enum RetryStrategy
    {
        Unknown,
        Exponential,
        Linear
    }
}