# MinimalAzureServiceBus

MinimalAzureServiceBus is a lightweight .NET library designed to provide essential functionalities for interacting with Azure Service Bus in a minimalistic and straightforward manner. It takes it's inspiration from Minimal Apis

## Features

- **(Planned) Send Messages**: Easily send messages to Azure Service Bus queues or topics.
- **Publish-Subscribe**: Implement publish-subscribe messaging patterns using topics and subscriptions.
- **(Planned) Dead-Letter Handling**: Handle dead-lettered messages gracefully for error recovery and analysis.
- **Message Retry**: Configure message retry policies to handle transient errors efficiently.
- **Simple Configuration**: Minimal setup and configuration required to start using Azure Service Bus.

## Installation

You can install the MinimalAzureServiceBus library via NuGet Package Manager:

```bash
dotnet add package MinimalAzureServiceBus
```

## Usage

To use this you will require a connection string to your ASB and an app name. Your handlers will be a async Func or method with the first parameter as your message type and any required services after that.

```c#

    using MinimalAzureServiceBus.Core;


    services.RegisterAzureServiceBusWorker(config.AzureServiceBusConnectionString, "my-app")
        .ProcessQueue(config.AzureServiceBusQueueName, HandleQueueMessage);

    public static Func<MyBasicMessage, Context, Task> HandleQueueMessage =
        async (message, context) =>
        {
            // Do some work
        };
```