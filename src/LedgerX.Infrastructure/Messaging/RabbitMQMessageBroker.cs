using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using LedgerX.Core.Interfaces;

namespace LedgerX.Infrastructure.Messaging
{
    public class RabbitMQMessageBroker : IMessageBroker, IDisposable
    {
        private readonly IConnection? _connection;
        private readonly IModel? _channel;
        private readonly ILogger<RabbitMQMessageBroker> _logger;
        private const string ExchangeName = "ledgerx.jobs.exchange";
        private const string DeadLetterExchangeName = "ledgerx.deadletter.exchange";

        public RabbitMQMessageBroker(IConfiguration configuration, ILogger<RabbitMQMessageBroker> logger)
        {
            _logger = logger;
            try
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(configuration.GetConnectionString("RabbitMQ") ?? "amqp://guest:guest@localhost:5672/")
                };

                _connection = factory.CreateConnection();
                _channel = _connection.CreateModel();

                // Declare exchanges
                _channel.ExchangeDeclare(ExchangeName, ExchangeType.Direct, durable: true);
                _channel.ExchangeDeclare(DeadLetterExchangeName, ExchangeType.Direct, durable: true);

                // Setup queues with DLQ configuration
                SetupQueue("ledgerx.marketdata.queue", "job.marketdata", "ledgerx.marketdata.dlq", "deadletter.marketdata");
                SetupQueue("ledgerx.analytics.queue", "job.analytics", "ledgerx.analytics.dlq", "deadletter.analytics");
                SetupQueue("ledgerx.alert.queue", "job.alert", "ledgerx.alert.dlq", "deadletter.alert");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect or initialize RabbitMQ topology. Messages cannot be published.");
            }
        }

        private void SetupQueue(string queueName, string routingKey, string dlqName, string dlqRoutingKey)
        {
            if (_channel == null) return;

            // 1. Declare the DLQ
            _channel.QueueDeclare(dlqName, durable: true, exclusive: false, autoDelete: false, arguments: null);
            _channel.QueueBind(dlqName, DeadLetterExchangeName, dlqRoutingKey, null);

            // 2. Declare the main queue with DLQ settings
            var args = new System.Collections.Generic.Dictionary<string, object>
            {
                { "x-dead-letter-exchange", DeadLetterExchangeName },
                { "x-dead-letter-routing-key", dlqRoutingKey }
            };

            _channel.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false, arguments: args);
            _channel.QueueBind(queueName, ExchangeName, routingKey, null);
        }

        public Task PublishJobAsync<T>(string routingKey, T payload)
        {
            if (_channel == null || _connection == null || !_connection.IsOpen)
            {
                _logger.LogWarning("RabbitMQ connection is unavailable. Cannot publish message for routing key: {RoutingKey}", routingKey);
                return Task.CompletedTask;
            }

            try
            {
                var json = JsonSerializer.Serialize(payload);
                var body = Encoding.UTF8.GetBytes(json);

                var properties = _channel.CreateBasicProperties();
                properties.Persistent = true;

                _channel.BasicPublish(
                    exchange: ExchangeName,
                    routingKey: routingKey,
                    basicProperties: properties,
                    body: body
                );

                _logger.LogInformation("Successfully published message to RabbitMQ with routing key: {RoutingKey}", routingKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while publishing message to RabbitMQ for routing key: {RoutingKey}", routingKey);
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            try
            {
                _channel?.Dispose();
                _connection?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing RabbitMQ components.");
            }
        }
    }
}
