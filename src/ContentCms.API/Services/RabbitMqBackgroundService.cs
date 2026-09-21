using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ContentCms.API.DTOs.Events;
using RabbitMQ.Client;

namespace ContentCms.API.Services
{
    public class RabbitMqBackgroundService : BackgroundService
    {
        private readonly Channel<ContentUpdateEvent> _channel;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RabbitMqBackgroundService> _logger;
        private IConnection? _connection;
        private IChannel? _rabbitChannel;

        public RabbitMqBackgroundService(
            Channel<ContentUpdateEvent> channel,
            IConfiguration configuration,
            ILogger<RabbitMqBackgroundService> logger)
        {
            _channel = channel;
            _configuration = configuration;
            _logger = logger;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            var factory = new ConnectionFactory
            {
                HostName = _configuration["RabbitMQ:HostName"] ?? "localhost",
                Port     = int.TryParse(_configuration["RabbitMQ:Port"], out var port) ? port : 5672,
                UserName = _configuration["RabbitMQ:UserName"] ?? "guest",
                Password = _configuration["RabbitMQ:Password"] ?? "guest"
            };

            try
            {
                _connection    = await factory.CreateConnectionAsync(cancellationToken);
                _rabbitChannel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

                var queueName = _configuration["RabbitMQ:QueueName"] ?? "CMSupdates";
                await _rabbitChannel.QueueDeclareAsync(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: cancellationToken);

                _logger.LogInformation("RabbitMQ connected to {Host}:{Port}, queue '{Queue}' ready.",
                    factory.HostName, factory.Port, queueName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to RabbitMQ at {Host}:{Port}. Events will NOT be published.",
                    factory.HostName, factory.Port);
            }

            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var queueName = _configuration["RabbitMQ:QueueName"] ?? "CMSupdates";

            try
            {
                await foreach (var contentEvent in _channel.Reader.ReadAllAsync(stoppingToken))
                {
                    if (_rabbitChannel == null || !_rabbitChannel.IsOpen)
                    {
                        _logger.LogWarning("RabbitMQ channel is not open. Cannot publish event.");
                        continue;
                    }

                    var message = JsonSerializer.Serialize(contentEvent);
                    var body = Encoding.UTF8.GetBytes(message);

                    await _rabbitChannel.BasicPublishAsync(
                        exchange: string.Empty,
                        routingKey: queueName,
                        mandatory: false,
                        body: body,
                        cancellationToken: stoppingToken);

                    _logger.LogInformation("Published event {EventType} for Content ID {ContentId} to RabbitMQ.", contentEvent.EventType, contentEvent.Id);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("RabbitMqBackgroundService is stopping.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred processing event queue.");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);

            if (_rabbitChannel != null)
                await _rabbitChannel.CloseAsync(cancellationToken);

            if (_connection != null)
                await _connection.CloseAsync(cancellationToken);
        }
    }
}
