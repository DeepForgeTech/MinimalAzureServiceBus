using Microsoft.Extensions.DependencyInjection;

namespace MinimalAzureServiceBus.Core
{
    public static class AzureServiceBusWorkerRegister
    {
        public static AzureServiceBusWorkerRegistration RegisterAzureServiceBusWorker(this IServiceCollection services, string ServiceBusConnectionString, string appName)
        {
            var registration = new AzureServiceBusWorkerRegistrationDetail(ServiceBusConnectionString, appName);

            services.AddSingleton(registration);
            services.AddHostedService<AzureServiceBusWorker>();

            return registration;
        }
    }
}