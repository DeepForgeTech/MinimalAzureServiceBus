using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;

namespace MinimalAzureServiceBus.Core
{
    public static class Extensions
    {
        public static async Task EnsureQueueExistsAsync(this ServiceBusAdministrationClient client, string queueName, ILogger? logger = null, CancellationToken cancellationToken = default)
        {
            if (await client.QueueExistsAsync(queueName, cancellationToken)) return;

            logger?.LogInformation("Queue {QueueName} does not exist, creating it now", queueName);

            await client.CreateQueueAsync(queueName, cancellationToken);
        }

        public static async Task EnsureTopicExistsAsync(this ServiceBusAdministrationClient client, string topicName, ILogger? logger = null, CancellationToken cancellationToken = default)
        {
            if (await client.TopicExistsAsync(topicName, cancellationToken)) return;

            logger?.LogInformation("Topic {TopicName} does not exist, creating it now", topicName);

            await client.CreateTopicAsync(topicName, cancellationToken);
        }

        public static async Task EnsureSubscriptionExistsAsync(this ServiceBusAdministrationClient client, string topicName, string subscriptionName, ILogger? logger = null, CancellationToken cancellationToken = default)
        {
            if (await client.SubscriptionExistsAsync(topicName, subscriptionName, cancellationToken)) return;

            logger?.LogInformation("Subscription {SubscriptionName} for Topic {TopicName} does not exist, creating it now", subscriptionName, topicName);

            await client.CreateSubscriptionAsync(topicName, subscriptionName, cancellationToken);
        }
    }
}