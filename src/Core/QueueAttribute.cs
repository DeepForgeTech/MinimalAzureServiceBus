namespace MinimalAzureServiceBus.Core
{
    public class QueueAttribute : MessagingAttribute
    {
        public QueueAttribute(string queueName = "") : base(queueName) { }
    }
}