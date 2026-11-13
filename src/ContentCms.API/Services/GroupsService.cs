using ContentCms.API.DTOs.Events;
using ContentCms.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentCms.API.Services
{
    public class GroupsService : IGroupsService
    {
        private readonly ContentCmsDbContext _context;
        private readonly IContentEventPublisher _events;

        public GroupsService(ContentCmsDbContext context, IContentEventPublisher events)
        {
            _context = context;
            _events = events;
        }

        private async Task PublishAllAsync(IEnumerable<ContentModel> contents, ContentEventType eventType)
        {
            foreach (var content in contents)
            {
                await _events.PublishAsync(content, eventType);
            }
        }

        public async Task<GroupModel> CreateGroupAsync(GroupModel group)
        {
            _context.Groups.Add(group);
            await _context.SaveChangesAsync();
            return group;
        }

        public async Task<GroupModel?> GetGroupByIdAsync(int id)
        {
            return await _context.Groups
                .Include(g => g.Contents)
                .FirstOrDefaultAsync(g => g.Id == id);
        }

        public async Task<List<GroupModel>> GetGroupsByOwnerIdAsync(int ownerId)
        {
            return await _context.Groups
                .Include(g => g.Contents)
                .Where(g => g.OwnerId == ownerId)
                .OrderByDescending(g => g.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<GroupModel>> GetAllGroupsAsync()
        {
            return await _context.Groups
                .Include(g => g.Owner)
                .Include(g => g.Contents)
                .OrderByDescending(g => g.CreatedAt)
                .ToListAsync();
        }

        public async Task<bool> UpdateGroupAsync(int id, string name, string? description)
        {
            var group = await _context.Groups.FindAsync(id);
            if (group == null) return false;

            group.Name = name;
            group.Description = description;
            await _context.SaveChangesAsync();

            // Consumers that mirror groups (e.g. as categories) re-read them on this event.
            var contents = await _context.Contents.Where(c => c.GroupId == id).ToListAsync();
            await PublishAllAsync(contents, ContentEventType.GroupChanged);
            return true;
        }

        public async Task<bool> DeleteGroupAsync(int id)
        {
            var group = await _context.Groups.FindAsync(id);
            if (group == null) return false;

            // Loaded so EF nulls their GroupId (SetNull) and the change can be published.
            var contents = await _context.Contents.Where(c => c.GroupId == id).ToListAsync();

            _context.Groups.Remove(group);
            await _context.SaveChangesAsync();

            await PublishAllAsync(contents, ContentEventType.GroupChanged);
            return true;
        }

        public async Task<bool> BulkToggleEnabledAsync(int groupId, bool enabled)
        {
            var contents = await _context.Contents.Where(c => c.GroupId == groupId).ToListAsync();
            foreach (var content in contents)
            {
                content.Enabled = enabled;
                content.UpdatedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
            await PublishAllAsync(contents, enabled ? ContentEventType.Enabled : ContentEventType.Disabled);
            return true;
        }

        public async Task<bool> BulkToggleVisibilityAsync(int groupId, bool isPublic)
        {
            var contents = await _context.Contents.Where(c => c.GroupId == groupId).ToListAsync();
            foreach (var content in contents)
            {
                content.IsPublic = isPublic;
                content.UpdatedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
            await PublishAllAsync(contents, isPublic ? ContentEventType.MadePublic : ContentEventType.MadePrivate);
            return true;
        }

        public async Task<bool> BulkToggleDeleteAsync(int groupId, bool isDeleted)
        {
            var contents = await _context.Contents.Where(c => c.GroupId == groupId).ToListAsync();
            foreach (var content in contents)
            {
                content.IsDeleted = isDeleted;
                content.DeletedAt = isDeleted ? DateTime.UtcNow : null;
                content.UpdatedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
            // A restore is announced as an edit: consumers re-read the item.
            await PublishAllAsync(contents, isDeleted ? ContentEventType.Deleted : ContentEventType.Edited);
            return true;
        }
    }
}
