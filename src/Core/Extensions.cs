using System.Threading.Tasks;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;

namespace MinimalAzureServiceBus.Core
{
    public static class Extensions
    {
        public static async Task EnsureQueueExistsAsync(this ServiceBusAdministrationClient client, string queueName, ILogger? logger = null)
        {
            if (await client.QueueExistsAsync(queueName)) return;

            logger?.LogInformation("Queue {QueueName} does not exist, creating it now", queueName);

            await client.CreateQueueAsync(queueName);
        }

        public static async Task EnsureTopicExistsAsync(this ServiceBusAdministrationClient client, string topicName, ILogger? logger = null)
        {
            if (await client.TopicExistsAsync(topicName)) return;

            logger?.LogInformation("Topic {TopicName} does not exist, creating it now", topicName);

            await client.CreateTopicAsync(topicName);
        }

        public static async Task EnsureSubscriptionExistsAsync(this ServiceBusAdministrationClient client, string topicName, string subscriptionName, ILogger? logger = null)
        {
            if (await client.SubscriptionExistsAsync(topicName, subscriptionName)) return;

            logger?.LogInformation("Subscription {SubscriptionName} for Topic {TopicName} does not exist, creating it now", subscriptionName, topicName);

            await client.CreateSubscriptionAsync(topicName, subscriptionName);
        }
    }
}