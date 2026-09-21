using System.Threading.Channels;
using ContentCms.API.DTOs.Events;

namespace ContentCms.API.Services
{
    public class RabbitMqEventBus : IRabbitMqEventBus
    {
        private readonly Channel<ContentUpdateEvent> _channel;

        public RabbitMqEventBus(Channel<ContentUpdateEvent> channel)
        {
            _channel = channel;
        }

        public async ValueTask PublishEventAsync(ContentUpdateEvent contentEvent, CancellationToken cancellationToken = default)
        {
            await _channel.Writer.WriteAsync(contentEvent, cancellationToken);
        }
    }
}
