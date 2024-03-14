using System;

namespace MinimalAzureServiceBus.Core.Models
{
    /// <summary>
    /// Used when processing cannot proceed due to unmet conditions and needs to be deferred.
    /// Use Case: The message arrives before its processing conditions are met (e.g., a dependency on another service or data not yet available).
    /// Handling: Reschedule the message for future processing using ASB's scheduled delivery feature or custom logic to requeue the message for later.
    /// Optional Delay: Specifies a delay before the message should be reprocessed.
    /// </summary>
    public class DeferredResult : MessageProcessingResult
    {
        public TimeSpan? Delay { get; private set; } // Optional delay before reprocessing
        public DateTime? ScheduleFor { get; private set; } // Optional specific moment in time for scheduling

        public DeferredResult(string message, TimeSpan? delay = null, DateTime? scheduleFor = null) : base(false, message)
        {
            Delay = delay;
            ScheduleFor = scheduleFor;
        }
    }
}