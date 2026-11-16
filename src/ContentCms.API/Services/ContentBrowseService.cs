using ContentCms.API.DTOs.Events;
using ContentCms.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentCms.API.Services
{
    public record GroupSummary(GroupModel Group, int ContentCount);

    public record ContentPage(List<ContentModel> Items, int TotalCount, int Page, int PageSize);

    public enum ContentAccessResult
    {
        Success,
        NotFound,
        Forbidden
    }

    /// <summary>Filters for <see cref="IContentBrowseService.GetContentAsync"/>.</summary>
    public record ContentQuery(int? GroupId = null, IReadOnlyCollection<int>? TagIds = null, bool MatchAllTags = false, int Page = 1, int PageSize = 20);

    /// <summary>
    /// Browsing and organising content for API clients (the mobile app). Same rules as the web panel:
    /// users see and change their own content, groups and tags; admins see and change everything.
    /// Soft-deleted content is never listed.
    /// </summary>
    public interface IContentBrowseService
    {
        Task<List<GroupSummary>> GetGroupsAsync(int userId, bool isAdmin);

        /// <summary>Content the caller may manage, newest first, with group, owner and tags loaded.</summary>
        Task<ContentPage> GetContentAsync(int userId, bool isAdmin, ContentQuery query);

        /// <summary>One content item with group, owner and tags, or null when missing / not the caller's.</summary>
        Task<ContentModel?> GetContentByIdAsync(int contentId, int userId, bool isAdmin);

        Task<ContentAccessResult> CanAccessGroupAsync(int groupId, int userId, bool isAdmin);

        /// <summary>
        /// Moves the content into <paramref name="groupId"/> (null = no group) and publishes <c>GroupChanged</c>.
        /// Users can only use their own groups for their own content.
        /// </summary>
        Task<ContentAccessResult> SetGroupAsync(int contentId, int? groupId, int userId, bool isAdmin);
    }

    public class ContentBrowseService : IContentBrowseService
    {
        public const int MaxPageSize = 100;

        private readonly ContentCmsDbContext _context;
        private readonly IContentEventPublisher _events;

        public ContentBrowseService(ContentCmsDbContext context, IContentEventPublisher events)
        {
            _context = context;
            _events = events;
        }

        private IQueryable<ContentModel> Manageable(int userId, bool isAdmin)
        {
            var query = _context.Contents.Where(c => !c.IsDeleted);
            return isAdmin ? query : query.Where(c => c.OwnerId == userId);
        }

        public async Task<List<GroupSummary>> GetGroupsAsync(int userId, bool isAdmin)
        {
            var groups = _context.Groups.AsQueryable();
            if (!isAdmin) groups = groups.Where(g => g.OwnerId == userId);

            var rows = await groups
                .Include(g => g.Owner)
                .OrderBy(g => g.Name)
                .ThenBy(g => g.Id)
                .Select(g => new { Group = g, Count = g.Contents.Count(c => !c.IsDeleted) })
                .ToListAsync();
            return rows.Select(r => new GroupSummary(r.Group, r.Count)).ToList();
        }

        public async Task<ContentPage> GetContentAsync(int userId, bool isAdmin, ContentQuery query)
        {
            int page = Math.Max(1, query.Page);
            int pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

            var items = Manageable(userId, isAdmin);
            if (query.GroupId is int groupId) items = items.Where(c => c.GroupId == groupId);
            items = TagRules.ApplyTagFilter(items, query.TagIds, query.MatchAllTags);

            int total = await items.CountAsync();
            var list = await items
                .Include(c => c.Group)
                .Include(c => c.Owner)
                .Include(c => c.Tags)
                .OrderByDescending(c => c.CreatedAt)
                .ThenByDescending(c => c.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .AsSplitQuery()
                .ToListAsync();

            return new ContentPage(list, total, page, pageSize);
        }

        public Task<ContentModel?> GetContentByIdAsync(int contentId, int userId, bool isAdmin) =>
            Manageable(userId, isAdmin)
                .Include(c => c.Group)
                .Include(c => c.Owner)
                .Include(c => c.Tags)
                .AsSplitQuery()
                .FirstOrDefaultAsync(c => c.Id == contentId);

        public async Task<ContentAccessResult> CanAccessGroupAsync(int groupId, int userId, bool isAdmin)
        {
            var group = await _context.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId);
            if (group == null) return ContentAccessResult.NotFound;
            return isAdmin || group.OwnerId == userId ? ContentAccessResult.Success : ContentAccessResult.Forbidden;
        }

        public async Task<ContentAccessResult> SetGroupAsync(int contentId, int? groupId, int userId, bool isAdmin)
        {
            var content = await _context.Contents.FirstOrDefaultAsync(c => c.Id == contentId && !c.IsDeleted);
            if (content == null) return ContentAccessResult.NotFound;
            if (!isAdmin && content.OwnerId != userId) return ContentAccessResult.Forbidden;

            if (groupId is int id)
            {
                var access = await CanAccessGroupAsync(id, userId, isAdmin);
                if (access != ContentAccessResult.Success) return access;
            }

            if (content.GroupId == groupId) return ContentAccessResult.Success;

            content.GroupId = groupId;
            content.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _events.PublishAsync(content, ContentEventType.GroupChanged);
            return ContentAccessResult.Success;
        }
    }
}
