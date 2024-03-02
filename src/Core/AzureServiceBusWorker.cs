using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MinimalAzureServiceBus.Core
{
    public sealed class AzureServiceBusWorker : BackgroundService
    {
        private ServiceBusClient? _serviceBusClient;
        private Dictionary<(string Name, string Type), ServiceBusProcessor> _processors = new Dictionary<(string Name, string Type), ServiceBusProcessor>();
        public CancellationTokenRegistration StoppingTokenRegistration;
        private readonly AzureServiceBusWorkerRegistrationDetail _registration;
        private readonly ILogger<AzureServiceBusWorkerRegistration> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public AzureServiceBusWorker(AzureServiceBusWorkerRegistrationDetail registration, ILogger<AzureServiceBusWorkerRegistration> logger, IServiceScopeFactory serviceScopeFactory)
        {
            _registration = registration;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Azure Service Bus Worker running at: {time}", DateTimeOffset.Now);

            await Task.WhenAll(_processors.Select(async detail =>
            {
                try
                {
                    await detail.Value.StartProcessingAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Exception thrown at {nameof(AzureServiceBusWorker)} ExecuteAsync task.");
                }
            }));

            await TaskCompletion(stoppingToken);

            _logger.LogInformation("Azure Service Bus Worker Stopping at: {time}", DateTimeOffset.Now);
        }

        private async Task TaskCompletion(CancellationToken stoppingToken)
        {
            var taskCompletionSource = new TaskCompletionSource<bool>();
            StoppingTokenRegistration = stoppingToken.Register(() => taskCompletionSource.SetResult(true));
            await taskCompletionSource.Task;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ASB Worker Started at: {time}", DateTimeOffset.Now);

            if (string.IsNullOrEmpty(_registration.ServiceBusConnectionString)) return base.StartAsync(cancellationToken);

            _serviceBusClient = new ServiceBusClient(_registration.ServiceBusConnectionString);

            foreach (var (key, handler) in _registration.HandlerRegistrations)
            {
                var (name, type) = key;

                if (!_processors.ContainsKey(key))
                {
                    var newProcessor = type switch
                    {
                        "Queue" => _serviceBusClient.CreateProcessor(name),
                        "Topic" => _serviceBusClient.CreateProcessor(name, _registration.AppName),

                        _ => throw new NotImplementedException($"{type} is not a valid message type. Values are \"Queue\" or \"Topic\"")
                    };

                    _processors.Add(key, newProcessor);

                    newProcessor.ProcessErrorAsync += ErrorHandlerAsync;
                }

                var processor = _processors[key];

                processor.ProcessMessageAsync += handler(_serviceScopeFactory);
            }

            return base.StartAsync(cancellationToken);
        }

        private Task ErrorHandlerAsync(ProcessErrorEventArgs arg) =>
            Task.Delay(0);

        public override async Task StopAsync(CancellationToken cancellationToken)
        {

            foreach (var (_, processor) in _processors)
            {
                await processor.StopProcessingAsync(cancellationToken);
                await processor.DisposeAsync();
            }

            if (_serviceBusClient != null)
                await _serviceBusClient.DisposeAsync();

            await base.StopAsync(cancellationToken);
            _logger.LogInformation("Azure Service Bus Worker Stopped at: {time}", DateTimeOffset.Now);
        }

    }
}