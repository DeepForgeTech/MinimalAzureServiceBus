namespace MinimalAzureServiceBus.Core.Models
{
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
}