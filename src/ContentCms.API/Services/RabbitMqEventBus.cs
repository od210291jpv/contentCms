using System.Threading.Channels;
using ContentCms.API.DTOs.Events;
using ContentCms.API.Services.Plugins;

namespace ContentCms.API.Services
{
    public class RabbitMqEventBus : IRabbitMqEventBus
    {
        private readonly Channel<ContentUpdateEvent> _channel;
        private readonly PluginEventQueue _pluginQueue;
        private readonly IPluginTokenService _pluginTokens;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<RabbitMqEventBus> _logger;

        public RabbitMqEventBus(
            Channel<ContentUpdateEvent> channel,
            PluginEventQueue pluginQueue,
            IPluginTokenService pluginTokens,
            IHttpContextAccessor httpContextAccessor,
            ILogger<RabbitMqEventBus> logger)
        {
            _channel = channel;
            _pluginQueue = pluginQueue;
            _pluginTokens = pluginTokens;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async ValueTask PublishEventAsync(ContentUpdateEvent contentEvent, CancellationToken cancellationToken = default)
        {
            await _channel.Writer.WriteAsync(contentEvent, cancellationToken);
            PublishToPlugins(contentEvent);
        }

        private void PublishToPlugins(ContentUpdateEvent contentEvent)
        {
            try
            {
                // If the change was made through the API by a plugin, remember which one so it is not re-triggered by itself.
                var authorization = _httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
                var origin = _pluginTokens.Validate(authorization)?.PluginId;

                _pluginQueue.Channel.Writer.TryWrite(new PluginEvent
                {
                    Name = PluginEventNames.From(contentEvent.EventType),
                    Content = contentEvent,
                    OriginPluginId = origin
                });
            }
            catch (Exception ex)
            {
                // Plugins must never break the core publishing flow.
                _logger.LogError(ex, "Failed to enqueue event for plugins.");
            }
        }
    }
}
