using System.Collections.Generic;
using System;

namespace MinimalAzureServiceBus.Core.Models
{
    /// <summary>
    /// Base class for message processing results.
    /// </summary>
    public abstract class MessageProcessingResult
    {
        public bool IsSuccess { get; protected set; }
        public string Message { get; protected set; }

        protected MessageProcessingResult(bool isSuccess, string message)
        {
            IsSuccess = isSuccess;
            Message = message;
        }
    }

    /// <summary>
    /// Represents a successful message processing outcome.
    /// Use Case: The message is processed as intended, and the operation completes without issues.
    /// Handling: Acknowledge the message so it's removed from the queue or subscription.
    /// </summary>
    public class SuccessResult : MessageProcessingResult
    {
        public SuccessResult(string message = "Processing succeeded.") : base(true, message)
        {
        }
    }

    /// <summary>
    /// Used for transient failures where a retry might succeed.
    /// Use Case: Temporary issues like network failures or throttling from a dependent service.
    /// Handling: Rely on ASB's built-in retry policies or implement custom logic to reschedule the message for a later attempt, possibly with exponential backoff.
    /// </summary>
    public class RetryableFailureResult : MessageProcessingResult
    {
        public RetryableFailureResult(string message) : base(false, message)
        {
        }
    }

    /// <summary>
    /// Indicates a non-recoverable failure where retries are not expected to succeed.
    /// Use Case: The message is malformed or the processing logic determines that it cannot be processed successfully under any circumstances.
    /// Handling: Log the error, send the message to a dead-letter queue for analysis, and possibly alert administrators.
    /// </summary>
    public class CompleteFailureResult : MessageProcessingResult
    {
        public CompleteFailureResult(string message) : base(false, message)
        {
        }
    }

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