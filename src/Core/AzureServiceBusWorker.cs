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
        private readonly ILogger<AzureServiceBusWorker> _logger;
        private readonly AzureServiceBusWorkerRegistrationDetail _registration;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly MessageSender _messageSender;

        private readonly MethodInfo processTaskResultMethod = typeof(AzureServiceBusWorker).GetMethod(nameof(ProcessTaskResult), BindingFlags.NonPublic | BindingFlags.Instance)!;
        private ServiceBusAdministrationClient? _adminClient;
        private readonly Dictionary<(string Name, ServiceBusType), ServiceBusProcessor> _processors = new Dictionary<(string Name, ServiceBusType Type), ServiceBusProcessor>();
        private ServiceBusClient? _serviceBusClient;
        public CancellationTokenRegistration StoppingTokenRegistration;

        public AzureServiceBusWorker(AzureServiceBusWorkerRegistrationDetail registration, ILoggerFactory loggerFactory, IServiceScopeFactory serviceScopeFactory)
        {
            _registration = registration;
            _logger = loggerFactory.CreateLogger<AzureServiceBusWorker>();
            _serviceScopeFactory = serviceScopeFactory;
            _messageSender = new MessageSender(_registration.ServiceBusConnectionString, loggerFactory.CreateLogger<MessageSender>());
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
                var (_, configuration) = _registration.DelegateHandlerRegistrations[key];

                if (!_processors.ContainsKey(key))
                {
                    ServiceBusProcessor? newProcessor;

                    var options = ProcessingConfiguration.ToServiceBusProcessorOptions(configuration);

                    switch (registrationType)
                    {
                        case ServiceBusType.Queue:
                            await _adminClient.EnsureQueueExistsAsync(name, _logger);

                            newProcessor = _serviceBusClient.CreateProcessor(name, options);

                            break;
                        case ServiceBusType.Topic:
                        {
                            await _adminClient.EnsureTopicExistsAsync(name, _logger);
                            await _adminClient.EnsureSubscriptionExistsAsync(name, _registration.AppName, _logger);

                            newProcessor = _serviceBusClient.CreateProcessor(name, _registration.AppName, options);

                            break;
                        }
                        case ServiceBusType.Unknown:
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
                    var messageType = args.Message.ApplicationProperties.ContainsKey("messageType") ? (string)args.Message.ApplicationProperties["messageType"] : null;
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

                        await SendToErrorQueue(_messageSender, errorQueueName, new CompleteFailureResult(new MaxRetriesExhaustedException(_registration.RetryConfiguration.MaxRetries)), args, messageType);

                        return;
                    }
                }

                await TryInvokeDelegateWithParameters(key, scope, args);
            };

        public async Task TryInvokeDelegateWithParameters((string name, ServiceBusType registrationType) key, AsyncServiceScope scope, ProcessMessageEventArgs args)
        {
            var watch = Stopwatch.StartNew();
            var (delegateToInvoke, _) = _registration.DelegateHandlerRegistrations[key];
            var delegateDetail = DelegateInfo.From(delegateToInvoke);
            var parameterValues = new Dictionary<string, object>();
            (string Name, Type Type)? unresolvedParam = null;
            Type? messageType = null;

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
                {
                    parameterValues[name] = deserializedObject;
                    messageType = type;
                }
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
            var processTask = genericMethod.Invoke(this, new[] {result, args, key, messageType?.AssemblyQualifiedName });

            if (processTask is Task processTaskResult)
                await processTaskResult;
        }

        private async Task SendToErrorQueue(IMessageSender sender, string errorQueueName, CompleteFailureResult result, ProcessMessageEventArgs args, string? messageType)
        {
            var errorMessage = new
            {
                OriginalMessage = args.Message.Body.ToString(),
                OriginalMessageType = messageType,
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

        private async Task ProcessTaskResult<T>(Task<T> task, ProcessMessageEventArgs args, (string name, ServiceBusType registrationType) key, string? messageType)
        {
            var result = await task;

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
                    retryMessage.ApplicationProperties.Add("messageType", messageType);

                    switch (key.registrationType)
                    {
                        case ServiceBusType.Queue:
                            await _messageSender.SendAsync(key.name, retryMessage);
                            break;
                        case ServiceBusType.Topic:
                            retryMessage.ApplicationProperties.Add("retrySourceEntityPath", args.EntityPath);

                            await _messageSender.PublishAsync(key.name, retryMessage);
                            break;
                        case ServiceBusType.Unknown:
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    break;
                case CompleteFailureResult completeFailureResult:
                    if (_registration.ErrorHandlingConfiguration is {SendUnhandledExceptionsToErrorQueue: true, ErrorQueueName: { }})
                    {
                        await SendToErrorQueue(_messageSender, _registration.ErrorHandlingConfiguration.ErrorQueueName, completeFailureResult, args, messageType);
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
                    message.ApplicationProperties.Add("messageType", messageType);

                    if (key.registrationType == ServiceBusType.Queue)
                        await _messageSender.SendAsync(key.name, message);
                    else if (key.registrationType == ServiceBusType.Topic)
                        await _messageSender.PublishAsync(key.name, message);

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