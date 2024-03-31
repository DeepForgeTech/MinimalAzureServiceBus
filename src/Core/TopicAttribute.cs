namespace MinimalAzureServiceBus.Core
{
    public class TopicAttribute : MessagingAttribute
    {
        public TopicAttribute(string topicName = "") : base(topicName) { }
    }
}