using ContentCms.API.DTOs.Events;
using ContentCms.API.Models;
using ContentCms.API.Pages.Shared;
using ContentCms.API.Services;
using ContentCms.API.Services.Plugins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ContentCms.API.Pages
{
    [Authorize(AuthenticationSchemes = "Cookies")]
    public class ContentManagementModel : PageModel
    {
        private readonly ContentCmsDbContext _context;
        private readonly IContentService _contentService;
        private readonly IPluginService _plugins;
        private readonly ITagsService _tagsService;
        private readonly IContentEventPublisher _events;

        public ContentManagementModel(ContentCmsDbContext context, IContentService contentService, IPluginService plugins, ITagsService tagsService, IContentEventPublisher events)
        {
            _events = events;
            _context = context;
            _contentService = contentService;
            _plugins = plugins;
            _tagsService = tagsService;
        }

        public List<ContentModel> Contents { get; set; } = new();
        public List<GroupModel> AvailableGroups { get; set; } = new();
        public List<TagModel> AvailableTags { get; set; } = new();
        public List<PluginModel> ContentActionPlugins { get; set; } = new();
        [BindProperty(SupportsGet = true, Name = "pageNumber")]
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public bool IsAdmin { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool? FilterEnabled { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool? FilterIsPublic { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool? FilterIsDeleted { get; set; }

        [BindProperty(SupportsGet = true)]
        public List<int> FilterTagIds { get; set; } = new();

        /// <summary>"all" = content must have every selected tag; anything else = at least one.</summary>
        [BindProperty(SupportsGet = true)]
        public string? FilterTagMode { get; set; }

        public bool FilterTagMatchAll => TagFilterQuery.IsMatchAll(FilterTagMode);

        [BindProperty(SupportsGet = true)]
        public string? SortBy { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool SortDescending { get; set; }

        public Dictionary<string, string> FilterParams
        {
            get
            {
                var dict = new Dictionary<string, string>();
                if (FilterEnabled.HasValue) dict["FilterEnabled"] = FilterEnabled.Value.ToString().ToLower();
                if (FilterIsPublic.HasValue) dict["FilterIsPublic"] = FilterIsPublic.Value.ToString().ToLower();
                if (FilterIsDeleted.HasValue) dict["FilterIsDeleted"] = FilterIsDeleted.Value.ToString().ToLower();
                TagFilterQuery.AddTo(dict, FilterTagIds, FilterTagMode, v => v);
                if (!string.IsNullOrEmpty(SortBy)) dict["SortBy"] = SortBy;
                dict["SortDescending"] = SortDescending.ToString().ToLower();
                dict["pageNumber"] = CurrentPage.ToString();
                return dict;
            }
        }

        private IActionResult RedirectToPageWithFilters()
        {
            var routeValues = new Dictionary<string, object>();
            if (FilterEnabled.HasValue) routeValues["FilterEnabled"] = FilterEnabled.Value;
            if (FilterIsPublic.HasValue) routeValues["FilterIsPublic"] = FilterIsPublic.Value;
            if (FilterIsDeleted.HasValue) routeValues["FilterIsDeleted"] = FilterIsDeleted.Value;
            TagFilterQuery.AddTo(routeValues, FilterTagIds, FilterTagMode, v => (object)v);
            if (!string.IsNullOrEmpty(SortBy)) routeValues["SortBy"] = SortBy;
            routeValues["SortDescending"] = SortDescending;
            if (CurrentPage > 1) routeValues["pageNumber"] = CurrentPage;
            
            return RedirectToPage(routeValues);
        }

        /// <summary>Link that filters the list by a single tag, keeping the other filters and sorting.</summary>
        public string? TagFilterUrl(int tagId)
        {
            var values = FilterParams.Where(kv => !kv.Key.StartsWith(TagFilterQuery.TagIdsKey) && kv.Key != TagFilterQuery.ModeKey && kv.Key != "pageNumber")
                                     .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
            values[$"{TagFilterQuery.TagIdsKey}[0]"] = tagId;
            return Url.Page("/ContentManagement", new RouteValueDictionary(values));
        }

        public async Task OnGetAsync(int pageNumber = 1)
        {
            CurrentPage = pageNumber < 1 ? 1 : pageNumber;
            int pageSize = 10;
            IsAdmin = User.IsInRole("Admin");
            string? userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int userId = userIdClaim != null ? int.Parse(userIdClaim) : 0;

            var query = _context.Contents.Include(c => c.Owner).Include(c => c.Group).Include(c => c.Tags).AsQueryable();

            if (!IsAdmin)
            {
                query = query.Where(c => c.OwnerId == userId);
            }

            // Apply Filters
            if (FilterEnabled.HasValue)
            {
                query = query.Where(c => c.Enabled == FilterEnabled.Value);
            }

            if (FilterIsPublic.HasValue)
            {
                query = query.Where(c => c.IsPublic == FilterIsPublic.Value);
            }

            if (FilterIsDeleted.HasValue)
            {
                query = query.Where(c => c.IsDeleted == FilterIsDeleted.Value);
            }

            query = TagRules.ApplyTagFilter(query, FilterTagIds, FilterTagMatchAll);

            // Apply Sorting
            switch (SortBy)
            {
                case "ID":
                    query = SortDescending ? query.OrderByDescending(c => c.Id) : query.OrderBy(c => c.Id);
                    break;
                case "OwnerId":
                    query = SortDescending ? query.OrderByDescending(c => c.OwnerId) : query.OrderBy(c => c.OwnerId);
                    break;
                case "Description":
                    query = SortDescending ? query.OrderByDescending(c => c.Description) : query.OrderBy(c => c.Description);
                    break;
                case "CreatedAt":
                    query = SortDescending ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt);
                    break;
                case "UpdatedAt":
                    query = SortDescending ? query.OrderByDescending(c => c.UpdatedAt) : query.OrderBy(c => c.UpdatedAt);
                    break;
                case "DeletedAt":
                    query = SortDescending ? query.OrderByDescending(c => c.DeletedAt) : query.OrderBy(c => c.DeletedAt);
                    break;
                default:
                    // Default sort
                    query = query.OrderByDescending(c => c.CreatedAt);
                    break;
            }

            int totalCount = await query.CountAsync();
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            if(TotalPages == 0) TotalPages = 1;

            Contents = await query
                .Skip((CurrentPage - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            if (User.HasClaim("GroupsEnabled", "true") || IsAdmin)
            {
                var groupsQuery = _context.Groups.AsQueryable();
                if (!IsAdmin) groupsQuery = groupsQuery.Where(g => g.OwnerId == userId);
                AvailableGroups = await groupsQuery.OrderBy(g => g.Name).ToListAsync();
            }

            AvailableTags = await _tagsService.GetAvailableTagsAsync(userId, IsAdmin);

            var views = await _plugins.GetViewsForUserAsync(userId, includeGloballyDisabled: false);
            ContentActionPlugins = views
                .Where(v => v.EffectiveEnabled && v.Plugin.Kind.HasFlag(PluginKind.ContentAction))
                .Select(v => v.Plugin)
                .ToList();
        }

        public async Task<IActionResult> OnPostUploadAsync(IFormFile file, string description, bool isPublic, int? groupId, int[]? tagIds)
        {
            if (file == null || file.Length == 0)
            {
                return RedirectToPage(new { error = "File is required." });
            }

            string? userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int userId = userIdClaim != null ? int.Parse(userIdClaim) : 0;

            var host = HttpContext.Request.Host.ToUriComponent();
            string imgDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "img");
            if (!Directory.Exists(imgDir)) Directory.CreateDirectory(imgDir);

            // Generate unique filename to avoid collisions
            string fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
            string path = Path.Combine(imgDir, fileName);

            using (var stream = new FileStream(path, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            string fileUrl = $"{HttpContext.Request.Scheme}://{host}/img/{fileName}";

            ContentModel contentModel = new ContentModel()
            {
                Description = description,
                IsPublic = isPublic,
                OwnerId = userId,
                Path = fileUrl,
                Enabled = true,
                GroupId = groupId
            };

            var created = await _contentService.CreateAsync(contentModel);
            if (tagIds is { Length: > 0 })
            {
                if (await _tagsService.SetContentTagsAsync(created.Id, tagIds, userId, User.IsInRole("Admin")) == TagOperationResult.Success)
                {
                    await _events.PublishAsync(created.Id, ContentEventType.TagsChanged);
                }
            }
            return RedirectToPageWithFilters();
        }

        public async Task<IActionResult> OnPostToggleStatusAsync(int id, bool enabled)
        {
            await _contentService.SetEnabledAsync(id, enabled);
            return RedirectToPageWithFilters();
        }

        public async Task<IActionResult> OnPostToggleVisibilityAsync(int id, bool isPublic)
        {
            var content = await _context.Contents.FindAsync(id);
            if (content != null)
            {
                bool changed = content.IsPublic != isPublic;
                content.IsPublic = isPublic;
                content.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                if (changed)
                {
                    await _events.PublishAsync(content, isPublic ? ContentEventType.MadePublic : ContentEventType.MadePrivate);
                }
            }
            return RedirectToPageWithFilters();
        }

        public async Task<IActionResult> OnPostUpdateDescriptionAsync(int id, string description)
        {
            var content = await _context.Contents.FindAsync(id);
            if (content != null)
            {
                content.Description = description;
                content.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await _events.PublishAsync(content, ContentEventType.Edited);
            }
            return RedirectToPageWithFilters();
        }

        public async Task<IActionResult> OnPostReassignAsync(int id, int newOwnerId)
        {
            if (!User.IsInRole("Admin")) return Forbid();

            var content = await _context.Contents.FindAsync(id);
            if (content != null)
            {
                content.OwnerId = newOwnerId;
                content.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await _events.PublishAsync(content, ContentEventType.Reassigned);
            }
            return RedirectToPageWithFilters();
        }

        public async Task<IActionResult> OnPostAssignGroupAsync(int id, int? groupId)
        {
            var content = await _context.Contents.FindAsync(id);
            if (content != null)
            {
                // Verify ownership or admin rights
                bool isAdmin = User.IsInRole("Admin");
                string? userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                int userId = userIdClaim != null ? int.Parse(userIdClaim) : 0;
                
                if (isAdmin || content.OwnerId == userId)
                {
                    content.GroupId = groupId;
                    content.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    await _events.PublishAsync(content, ContentEventType.GroupChanged);
                }
            }
            return RedirectToPageWithFilters();
        }

        public async Task<IActionResult> OnPostSetTagsAsync(int contentId, int[]? tagIds)
        {
            string? userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int userId = userIdClaim != null ? int.Parse(userIdClaim) : 0;

            var result = await _tagsService.SetContentTagsAsync(contentId, tagIds ?? Array.Empty<int>(), userId, User.IsInRole("Admin"));
            if (result == TagOperationResult.Success)
            {
                await _events.PublishAsync(contentId, ContentEventType.TagsChanged);
                TempData["ActionMessage"] = "Tags updated.";
            }
            else TempData["ActionError"] = TagRules.ErrorMessage(result);

            return RedirectToPageWithFilters();
        }

        public async Task<IActionResult> OnPostSoftDeleteAsync(int id)
        {
            await _contentService.SoftDeleteAsync(id);
            return RedirectToPageWithFilters();
        }

        public async Task<IActionResult> OnPostExecutePluginActionAsync(int contentId, int pluginId)
        {
            string? userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int userId = userIdClaim != null ? int.Parse(userIdClaim) : 0;

            var content = await _context.Contents.FindAsync(contentId);
            if (content == null) return NotFound();

            bool isAdmin = User.IsInRole("Admin");
            if (!isAdmin && content.OwnerId != userId) return Forbid();

            var success = await _plugins.ExecuteContentActionAsync(pluginId, userId, content);
            if (success)
            {
                TempData["ActionMessage"] = "Plugin action executed successfully.";
            }
            else
            {
                TempData["ActionError"] = "Failed to execute plugin action or plugin not found.";
            }

            return RedirectToPageWithFilters();
        }
    }
}
