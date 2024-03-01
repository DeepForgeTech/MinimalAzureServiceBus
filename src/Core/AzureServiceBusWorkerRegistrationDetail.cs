using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;

namespace MinimalAzureServiceBus.Core
{
    internal class AzureServiceBusWorkerRegistrationDetail : AzureServiceBusWorkerRegistration
    {
        public AzureServiceBusWorkerRegistrationDetail(string serviceBusConnectionString, string appName) : base(serviceBusConnectionString, appName)
        {
        }

        internal string ServiceBusConnectionString => _serviceBusConnectionString;
        internal string AppName => _appName;
        internal Dictionary<(string Name, string Type), Func<IServiceScopeFactory, Func<ProcessMessageEventArgs, Task>>> HandlerRegistrations => _handlerRegistrations;
    }
}