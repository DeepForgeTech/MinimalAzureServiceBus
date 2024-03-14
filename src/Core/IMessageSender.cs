using Azure.Messaging.ServiceBus;
using System.Text.Json;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MinimalAzureServiceBus.Core
{
    public interface IMessageSender
    {
        Task SendAsync<TMessage>(string queueOrTopicName, TMessage message) where TMessage : class;
    }

    public class MessageSender : IMessageSender
    {
        private readonly string _serviceBusConnectionString;
        private readonly ILogger<MessageSender> _logger;

        public MessageSender(string serviceBusConnectionString, ILogger<MessageSender> logger)
        {
            _serviceBusConnectionString = serviceBusConnectionString;
            _logger = logger;
        }

        public async Task SendAsync<TMessage>(string queueOrTopicName, TMessage message) where TMessage : class
        {
            await using var client = new ServiceBusClient(_serviceBusConnectionString);
            await using var sender = client.CreateSender(queueOrTopicName);

            try
            {
                // Serialize the object to JSON
                var messageBody = JsonSerializer.Serialize(message);

                // Create a message and send it
                var serviceBusMessage = new ServiceBusMessage(messageBody);

                await sender.SendMessageAsync(serviceBusMessage);

                _logger.LogTrace("Message sent to queue Or topic named {Name}", queueOrTopicName);
            }
            catch (Exception e)
            {
                _logger.LogError("An error occurred while sending message sent to queue Or topic named {Name}. Type: {Type}. Message: {Message}", queueOrTopicName, e.GetType().Name, e.Message);

                throw;
            }
        }
    }
}