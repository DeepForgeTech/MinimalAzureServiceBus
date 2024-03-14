namespace MinimalAzureServiceBus.Core.Models
{
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
}