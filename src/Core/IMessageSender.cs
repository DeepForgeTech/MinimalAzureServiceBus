using Azure.Messaging.ServiceBus;
using System.Text.Json;
using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;
using MinimalAzureServiceBus.Core.Models;

namespace MinimalAzureServiceBus.Core
{
    public interface IMessageSender
    {
        Task SendAsync<TMessage>(string queueName, TMessage message) where TMessage : class;
        Task PublishAsync<TMessage>(string topicName, TMessage message) where TMessage : class;
        Task SendAsync(string queueName, ServiceBusMessage message);
        Task PublishAsync(string topicName, ServiceBusMessage message);
    }

    public class MessageSender : IMessageSender
    {
        private readonly string _serviceBusConnectionString;
        private readonly ILogger<MessageSender> _logger;
        private readonly ServiceBusAdministrationClient _adminClient;

        public MessageSender(string serviceBusConnectionString, ILogger<MessageSender> logger)
        {
            _serviceBusConnectionString = serviceBusConnectionString;
            _adminClient = new ServiceBusAdministrationClient(serviceBusConnectionString);
            _logger = logger;
        }

        public async Task SendAsync<TMessage>(string queueName, TMessage message) where TMessage : class => 
            await SendAsync(queueName, ServiceBusType.Queue, message);

        public async Task PublishAsync<TMessage>(string topicName, TMessage message) where TMessage : class => 
            await SendAsync(topicName, ServiceBusType.Topic, message);

        public async Task SendAsync(string queueName, ServiceBusMessage serviceBusMessage) =>
            await SendAsync(queueName, ServiceBusType.Queue, serviceBusMessage);
        public async Task PublishAsync(string topicName, ServiceBusMessage serviceBusMessage) =>
            await SendAsync(topicName, ServiceBusType.Topic, serviceBusMessage);

        async Task SendAsync<TMessage>(string name, ServiceBusType messageType, TMessage message) where TMessage : class
        {
            await using var client = new ServiceBusClient(_serviceBusConnectionString);
            await using var sender = client.CreateSender(name);

            // Serialize the object to JSON
            var messageBody = JsonSerializer.Serialize(message);

            await SendAsync(name, messageType, new ServiceBusMessage(messageBody));
        }

        async Task SendAsync(string queueOrTopicName, ServiceBusType messageType, ServiceBusMessage serviceBusMessage)
        {
            await using var client = new ServiceBusClient(_serviceBusConnectionString);

            switch (messageType)
            {
                case ServiceBusType.Queue:
                    await _adminClient.EnsureQueueExistsAsync(queueOrTopicName);
                    break;
                case ServiceBusType.Topic:
                    await _adminClient.EnsureTopicExistsAsync(queueOrTopicName);
                    break;
                case ServiceBusType.Unknown:
                default:
                    throw new ArgumentException("Invalid message type", nameof(messageType));
            }

            await using var sender = client.CreateSender(queueOrTopicName);

            try
            {
                await sender.SendMessageAsync(serviceBusMessage);

                _logger.LogTrace("Message sent to queue Or topic named {Name}", queueOrTopicName);
            }
            catch (ServiceBusException) // Being Throttled, TODO: check specific error code
            {
                // No need to log, leave to client
                throw;
            }
            catch (Exception e)
            {
                _logger.LogError("An unhandled exception occurred while sending message sent to queue Or topic named {Name}. Type: {Type}. Message: {Message}", queueOrTopicName, e.GetType().Name, e.Message);

                throw;
            }
        }
    }
}