using ContentCms.API.DTOs.Events;

namespace ContentCms.API.Services
{
    public interface IRabbitMqEventBus
    {
        ValueTask PublishEventAsync(ContentUpdateEvent contentEvent, CancellationToken cancellationToken = default);
    }
}
