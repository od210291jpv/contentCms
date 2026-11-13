using ContentCms.API.DTOs.Events;
using ContentCms.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentCms.API.Services
{
    public class ContentEventPublisher : IContentEventPublisher
    {
        private readonly ContentCmsDbContext _context;
        private readonly IRabbitMqEventBus _eventBus;
        private readonly ILogger<ContentEventPublisher> _logger;

        public ContentEventPublisher(ContentCmsDbContext context, IRabbitMqEventBus eventBus, ILogger<ContentEventPublisher> logger)
        {
            _context = context;
            _eventBus = eventBus;
            _logger = logger;
        }

        public async Task PublishAsync(ContentModel content, ContentEventType eventType)
        {
            try
            {
                var tagIds = await _context.Contents
                    .Where(c => c.Id == content.Id)
                    .SelectMany(c => c.Tags)
                    .Select(t => t.Id)
                    .ToListAsync();

                await _eventBus.PublishEventAsync(new ContentUpdateEvent
                {
                    EventType = eventType,
                    Id = content.Id,
                    OwnerId = content.OwnerId,
                    GroupId = content.GroupId,
                    TagIds = tagIds,
                    Enabled = content.Enabled,
                    Description = content.Description,
                    Path = content.Path,
                    IsPublic = content.IsPublic,
                    IsDeleted = content.IsDeleted,
                    CreatedAt = content.CreatedAt,
                    UpdatedAt = content.UpdatedAt,
                    DeletedAt = content.DeletedAt
                });
            }
            catch (Exception ex)
            {
                // Event delivery must never break the change that triggered it.
                _logger.LogError(ex, "Failed to publish {EventType} for content {ContentId}.", eventType, content.Id);
            }
        }

        public async Task PublishAsync(int contentId, ContentEventType eventType)
        {
            var content = await _context.Contents.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contentId);
            if (content != null)
            {
                await PublishAsync(content, eventType);
            }
        }
    }
}
