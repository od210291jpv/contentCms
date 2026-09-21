using ContentCms.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentCms.API.Services
{
    public class GroupsService : IGroupsService
    {
        private readonly ContentCmsDbContext _context;

        public GroupsService(ContentCmsDbContext context)
        {
            _context = context;
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
            return true;
        }

        public async Task<bool> DeleteGroupAsync(int id)
        {
            var group = await _context.Groups.FindAsync(id);
            if (group == null) return false;

            _context.Groups.Remove(group);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> BulkToggleEnabledAsync(int groupId, bool enabled)
        {
            var contents = await _context.Contents.Where(c => c.GroupId == groupId).ToListAsync();
            foreach (var content in contents)
            {
                content.Enabled = enabled;
            }
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> BulkToggleVisibilityAsync(int groupId, bool isPublic)
        {
            var contents = await _context.Contents.Where(c => c.GroupId == groupId).ToListAsync();
            foreach (var content in contents)
            {
                content.IsPublic = isPublic;
            }
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> BulkToggleDeleteAsync(int groupId, bool isDeleted)
        {
            var contents = await _context.Contents.Where(c => c.GroupId == groupId).ToListAsync();
            foreach (var content in contents)
            {
                content.IsDeleted = isDeleted;
                content.DeletedAt = isDeleted ? DateTime.UtcNow : null;
            }
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
