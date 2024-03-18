using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MinimalAzureServiceBus.Core.Models;

namespace MinimalAzureServiceBus.Core
{
    public sealed class AzureServiceBusWorker : BackgroundService
    {
        private readonly ILogger<AzureServiceBusWorkerRegistration> _logger;
        private readonly AzureServiceBusWorkerRegistrationDetail _registration;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private readonly MethodInfo processTaskResultMethod = typeof(AzureServiceBusWorker).GetMethod(nameof(ProcessTaskResult), BindingFlags.NonPublic | BindingFlags.Instance)!;
        private ServiceBusAdministrationClient? _adminClient;
        private readonly Dictionary<(string Name, ServiceBusType), ServiceBusProcessor> _processors = new Dictionary<(string Name, ServiceBusType Type), ServiceBusProcessor>();
        private ServiceBusClient? _serviceBusClient;
        public CancellationTokenRegistration StoppingTokenRegistration;

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

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ASB Worker Started at: {time}", DateTimeOffset.Now);

            if (string.IsNullOrEmpty(_registration.ServiceBusConnectionString))
                await base.StartAsync(cancellationToken);

            _serviceBusClient = new ServiceBusClient(_registration.ServiceBusConnectionString);
            _adminClient = new ServiceBusAdministrationClient(_registration.ServiceBusConnectionString);

            foreach (var (name, registrationType) in _registration.DelegateHandlerRegistrations.Keys)
            {
                var key = (name, registrationType);

                if (!_processors.ContainsKey(key))
                {
                    ServiceBusProcessor? newProcessor;

                    switch (registrationType)
                    {
                        case ServiceBusType.Queue:
                            await _adminClient.EnsureQueueExistsAsync(name, _logger);

                            newProcessor = _serviceBusClient.CreateProcessor(name);

                            break;
                        case ServiceBusType.Topic:
                        {
                            await _adminClient.EnsureTopicExistsAsync(name, _logger);
                            await _adminClient.EnsureSubscriptionExistsAsync(name, _registration.AppName, _logger);

                            newProcessor = _serviceBusClient.CreateProcessor(name, _registration.AppName);

                            break;
                        }
                        default:
                            throw new NotImplementedException($"{registrationType} is not a valid message type. Values are \"Queue\" or \"Topic\"");
                    }

                    _processors.Add(key, newProcessor);

                    newProcessor.ProcessErrorAsync += ErrorHandlerAsync;
                }

                var processor = _processors[key];

                processor.ProcessMessageAsync += HandleDelegate(key, _serviceScopeFactory, cancellationToken);
            }

            await base.StartAsync(cancellationToken);
        }

        public Func<ProcessMessageEventArgs, Task> HandleDelegate((string name, ServiceBusType registrationType) key, IServiceScopeFactory scopeFactory, CancellationToken cancellationToken) =>
            async args =>
            {
                var scope = scopeFactory.CreateAsyncScope();

                if (args.Message.ApplicationProperties.ContainsKey("retryCount"))
                {
                    var retryCount = (int) args.Message.ApplicationProperties["retryCount"];
                    var retrySourceEntityPath = args.Message.ApplicationProperties.ContainsKey("retrySourceEntityPath") ? (string) args.Message.ApplicationProperties["retrySourceEntityPath"] : null;
                    var currentEntityPath = args.EntityPath;

                    if (retrySourceEntityPath != null && currentEntityPath != retrySourceEntityPath)
                        return;

                    if (retryCount >= _registration.RetryConfiguration.MaxRetries)
                    {
                        var errorQueueName = _registration.ErrorHandlingConfiguration.ErrorQueueName;

                        if (errorQueueName == null)
                        {
                            await args.DeadLetterMessageAsync(args.Message, cancellationToken: cancellationToken);

                            return;
                        }

                        await SendToErrorQueue(scope.ServiceProvider.GetRequiredService<IMessageSender>(), errorQueueName, new CompleteFailureResult(new MaxRetriesExhaustedException(_registration.RetryConfiguration.MaxRetries)), args);

                        return;
                    }
                }

                await TryInvokeDelegateWithParameters(key, scope, args);
            };

        public async Task TryInvokeDelegateWithParameters((string name, ServiceBusType registrationType) key, AsyncServiceScope scope, ProcessMessageEventArgs args)
        {
            var watch = Stopwatch.StartNew();
            var del = _registration.DelegateHandlerRegistrations[key];
            var delegateDetail = DelegateInfo.From(del);
            var parameterValues = new Dictionary<string, object>();
            (string Name, Type Type)? unresolvedParam = null;

            foreach (var (paramName, type) in delegateDetail.Parameters)
            {
                var service = scope.ServiceProvider.GetService(type);

                if (service != null)
                    parameterValues[paramName] = service;
                else if (unresolvedParam != null)
                    throw new InvalidOperationException("Unable to type to serialize the message as.");
                else
                    unresolvedParam = (paramName, type);
            }

            if (delegateDetail.Parameters.Count - 1 == parameterValues.Count && unresolvedParam != null)
            {
                var (name, type) = unresolvedParam.Value;
                var deserializedObject = JsonSerializer.Deserialize(args.Message.Body.ToString(), type, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (deserializedObject != null)
                    parameterValues[name] = deserializedObject;
                else
                    throw new InvalidOperationException($"Failed to deserialize the message body to the required parameter type: {type}.");
            }

            object result;

            try
            {
                result = delegateDetail.InvokeWith(parameterValues);

                if (!(result is Task taskResult))
                {
                    result = Task.FromResult(new SuccessResult());
                }
                else if (delegateDetail.InnerReturnType == null)
                {
                    await taskResult; // Await the task to ensure completion

                    result = Task.FromResult(new SuccessResult());
                }
            }
            catch (Exception ex)
            {
                result = Task.FromResult(new CompleteFailureResult(ex));
            }

            // Create a method call to ProcessTaskResult<T> using reflection
            var genericMethod = processTaskResultMethod.MakeGenericMethod(result.GetType().GetGenericArguments()[0]);

            // Since we're calling an async method, we need to await it. However, MethodInfo.Invoke returns an object, so we need to cast it to Task to await.
            var processTask = genericMethod.Invoke(this, new[] {result, scope, args, key});

            if (processTask is Task processTaskResult)
                await processTaskResult;
        }

        private async Task SendToErrorQueue(IMessageSender sender, string errorQueueName, CompleteFailureResult result, ProcessMessageEventArgs args)
        {
            var errorMessage = new
            {
                OriginalMessage = args.Message.Body.ToString(),
                OriginatingEntityPath = args.EntityPath,
                OriginatingApp = _registration.AppName,
                ExceptionMessage = result.Message,
                ExceptionType = result.Exception.GetType().FullName,
                ExceptionStackTrace = result.Exception.StackTrace,
                InnerExceptionMessage = result.Exception.InnerException?.Message,
                Occurred = DateTimeOffset.UtcNow
            };

            var errorMessageJson = JsonSerializer.Serialize(errorMessage);

            // Create and send a new message to the error queue
            var errorQueueMessage = new ServiceBusMessage(errorMessageJson)
            {
                ContentType = "application/json"
            };

            await sender.SendAsync(errorQueueName, errorQueueMessage);
        }

        private async Task ProcessTaskResult<T>(Task<T> task, AsyncServiceScope scope, ProcessMessageEventArgs args, (string name, ServiceBusType registrationType) key)
        {
            var result = await task;
            var sender = scope.ServiceProvider.GetRequiredService<IMessageSender>();

            switch (result)
            {
                // generate arms to check if result is any of the types that inherit from MessageProcessingResult
                case SuccessResult successResult:
                    // Do nothing for now
                    break;
                case RetryableFailureResult failureResult:
                    // handle the result

                    var retryMessage = new ServiceBusMessage(args.Message.Body);
                    var retryCount = args.Message.ApplicationProperties.ContainsKey("retryCount") ? (int) args.Message.ApplicationProperties["retryCount"] : 0;

                    retryMessage.ApplicationProperties.Add("retryCount", retryCount);
                    retryMessage.ApplicationProperties.Add("lastError", failureResult.Message);

                    if (key.registrationType == ServiceBusType.Queue)
                    {
                        await sender.SendAsync(key.name, retryMessage);
                    }
                    else if (key.registrationType == ServiceBusType.Topic)
                    {
                        retryMessage.ApplicationProperties.Add("retrySourceEntityPath", args.EntityPath);

                        await sender.PublishAsync(key.name, retryMessage);
                    }

                    break;
                case CompleteFailureResult completeFailureResult:
                    if (_registration.ErrorHandlingConfiguration is {SendUnhandledExceptionsToErrorQueue: true, ErrorQueueName: { }})
                    {
                        await SendToErrorQueue(sender, _registration.ErrorHandlingConfiguration.ErrorQueueName, completeFailureResult, args);
                    }
                    else
                    {
                        await args.DeadLetterMessageAsync(args.Message);

                        return;
                    }

                    break;
                case DeferredResult deferredResult:
                    var newDateTime = DateTimeOffset.UtcNow;

                    if (deferredResult.ScheduleFor.HasValue)
                        newDateTime = deferredResult.ScheduleFor.Value;
                    else if (deferredResult.Delay.HasValue)
                        newDateTime = DateTimeOffset.UtcNow.Add(deferredResult.Delay.Value);

                    var message = new ServiceBusMessage(args.Message.Body);

                    message.ScheduledEnqueueTime = newDateTime;
                    message.ApplicationProperties.Add("deferred", true);

                    if (key.registrationType == ServiceBusType.Queue)
                        await sender.SendAsync(key.name, message);
                    else if (key.registrationType == ServiceBusType.Topic)
                        await sender.PublishAsync(key.name, message);

                    break;
            }

            await args.CompleteMessageAsync(args.Message);
        }

        private Task ErrorHandlerAsync(ProcessErrorEventArgs arg)
        {
            _logger.LogError(arg.Exception, "An error occurred while processing a message.");

            return Task.Delay(0);
        }

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