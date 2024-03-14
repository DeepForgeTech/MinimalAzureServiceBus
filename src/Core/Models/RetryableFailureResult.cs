namespace MinimalAzureServiceBus.Core.Models
{
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
}