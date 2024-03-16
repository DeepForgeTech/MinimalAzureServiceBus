using System;

namespace MinimalAzureServiceBus.Core.Models
{
    public class MaxRetriesExhaustedException : Exception
    {
        public MaxRetriesExhaustedException(int maxRetries) : base($"The maximum number of retries ({maxRetries}) has been exhausted.") { }
    }
}