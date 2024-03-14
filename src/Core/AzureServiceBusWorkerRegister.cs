using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MinimalAzureServiceBus.Core
{
    public static class AzureServiceBusWorkerRegister
    {
        public static AzureServiceBusWorkerRegistration RegisterAzureServiceBusWorker(this IServiceCollection services, string ServiceBusConnectionString, string appName)
        {
            var registration = new AzureServiceBusWorkerRegistrationDetail(ServiceBusConnectionString, appName);

            services.AddSingleton(registration);
            services.AddScoped<IMessageSender>(sp => new MessageSender(ServiceBusConnectionString, sp.GetRequiredService<ILogger<MessageSender>>()));
            services.AddHostedService<AzureServiceBusWorker>();

            return registration;
        }
    }
}