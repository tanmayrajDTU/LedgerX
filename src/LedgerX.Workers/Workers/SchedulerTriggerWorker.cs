using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using LedgerX.Core.Interfaces;
using LedgerX.Infrastructure.Services;

namespace LedgerX.Workers.Workers
{
    public class SchedulerTriggerWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SchedulerTriggerWorker> _logger;
        private readonly IConfiguration _configuration;
        private IConnection? _connection;
        private IModel? _channel;
        private const string QueueName = "ledgerx.scheduler.queue";
        private const string ExchangeName = "ledgerx.jobs.exchange";

        public SchedulerTriggerWorker(
            IServiceProvider serviceProvider,
            ILogger<SchedulerTriggerWorker> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("SchedulerTriggerWorker background service starting...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var factory = new ConnectionFactory
                    {
                        Uri = new Uri(_configuration.GetConnectionString("RabbitMQ") ?? "amqp://guest:guest@localhost:5672/")
                    };
                    _connection = factory.CreateConnection();
                    _channel = _connection.CreateModel();
                    
                    // Declare and bind the manual trigger queue
                    _channel.QueueDeclare(QueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
                    _channel.QueueBind(QueueName, ExchangeName, "job.scheduler.trigger", null);

                    _logger.LogInformation("SchedulerTriggerWorker successfully connected to RabbitMQ and bound trigger queue.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("SchedulerTriggerWorker failed to connect to RabbitMQ. Retrying in 5 seconds...");
                    await Task.Delay(5000, stoppingToken);
                }
            }

            if (_channel == null) return;

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                _logger.LogInformation("Manual scheduler trigger message received from RabbitMQ.");
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var scheduler = scope.ServiceProvider.GetRequiredService<PipelineJobScheduler>();
                    var cacheService = scope.ServiceProvider.GetRequiredService<ICacheService>();

                    // Read current config from Redis
                    bool injectFault = await cacheService.GetAsync<bool>("config:marketdata:inject-fault");

                    await scheduler.TriggerPipelineJobsAsync(injectFault);
                    _logger.LogInformation("Manual scheduler trigger successfully processed.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to run manually triggered scheduler pipeline.");
                }
                finally
                {
                    _channel.BasicAck(ea.DeliveryTag, false);
                }
            };

            _channel.BasicConsume(queue: QueueName, autoAck: false, consumer: consumer);

            var tcs = new TaskCompletionSource<bool>();
            stoppingToken.Register(() => tcs.SetResult(true));
            await tcs.Task;
        }

        public override void Dispose()
        {
            _channel?.Dispose();
            _connection?.Dispose();
            base.Dispose();
        }
    }
}
