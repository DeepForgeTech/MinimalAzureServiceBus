using Azure.Messaging.ServiceBus;
using System;

namespace MinimalAzureServiceBus.Core.Models
{
    /// <summary>
    /// Represents the processing configuration options for Azure Service Bus message handlers.
    /// </summary>
    public class ProcessingConfiguration
    {
        /// <summary>
        /// Gets or sets the number of messages to prefetch from the queue or subscription.
        /// Prefetching messages can improve throughput by reducing the number of round-trips
        /// to the service. A value of 0 disables prefetching.
        /// </summary>
        public int PrefetchCount { get; set; } = 0;

        /// <summary>
        /// Gets or sets the maximum number of concurrent calls to the message handler.
        /// Increasing this value can improve message processing throughput but requires more
        /// resources (CPU, memory). The default is 1.
        /// </summary>
        public int MaxConcurrentCalls { get; set; } = 1;

        /// <summary>
        /// Gets or sets the lock duration for messages during processing. This overrides
        /// the default lock duration set on the queue or subscription. Specify null to use
        /// the default lock duration.
        /// </summary>
        public TimeSpan? LockDuration { get; set; } = null;

        /// <summary>
        /// Gets or sets the maximum duration the processing host will automatically renew the
        /// lock for a message being processed. This is useful for long-running processing tasks.
        /// The default is 5 minutes.
        /// </summary>
        public TimeSpan MaxAutoLockRenewalDuration { get; set; } = TimeSpan.FromMinutes(5);

        ///// <summary>
        ///// Specifies whether session-based message processing is enabled. When enabled, the processor
        ///// will only receive messages from the same session until the session is complete. This is
        ///// important for scenarios requiring message ordering or session affinity.
        ///// The default is false.
        ///// </summary>
        //public bool SessionHandlingEnabled { get; set; } = false;

        // Define the function to transform ProcessingConfiguration into ServiceBusProcessorOptions
        public static Func<ProcessingConfiguration, ServiceBusProcessorOptions> ToServiceBusProcessorOptions = processingConfig =>
        {
            var processorOptions = new ServiceBusProcessorOptions();

            if (processingConfig.PrefetchCount > 0)
                processorOptions.PrefetchCount = processingConfig.PrefetchCount;

            processorOptions.MaxConcurrentCalls = processingConfig.MaxConcurrentCalls;

            if (processingConfig.LockDuration.HasValue)
                processorOptions.MaxAutoLockRenewalDuration = processingConfig.LockDuration.Value;

            processorOptions.MaxAutoLockRenewalDuration = processingConfig.MaxAutoLockRenewalDuration;

            return processorOptions;
        };
    }

}