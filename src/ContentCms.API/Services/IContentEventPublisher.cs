using ContentCms.API.DTOs.Events;
using ContentCms.API.Models;

namespace ContentCms.API.Services
{
    /// <summary>
    /// Builds a <see cref="ContentUpdateEvent"/> from the current state of a content item (including its
    /// group and tag ids) and publishes it to RabbitMQ and the plugin dispatcher.
    /// </summary>
    public interface IContentEventPublisher
    {
        Task PublishAsync(ContentModel content, ContentEventType eventType);

        /// <summary>Loads the content by id (deleted items included) and publishes; does nothing if it does not exist.</summary>
        Task PublishAsync(int contentId, ContentEventType eventType);
    }
}
