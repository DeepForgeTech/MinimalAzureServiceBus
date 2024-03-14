using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

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
        internal Dictionary<(string Name, string Type), Func<AsyncServiceScope, string, Task>> HandlerRegistrations => _handlerRegistrations;
    }
}