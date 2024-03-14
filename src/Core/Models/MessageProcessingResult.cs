using System.Collections.Generic;

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
}