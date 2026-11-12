using ContentCms.API.Models;
using ContentCms.API.Pages.Shared;
using ContentCms.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ContentCms.API.Pages
{
    /// <summary>Lists the content assigned to one group, with tag filtering.</summary>
    [Authorize(AuthenticationSchemes = "Cookies")]
    public class GroupContentModel : PageModel
    {
        private const int PageSize = 10;

        private readonly ContentCmsDbContext _context;
        private readonly ITagsService _tagsService;

        public GroupContentModel(ContentCmsDbContext context, ITagsService tagsService)
        {
            _context = context;
            _tagsService = tagsService;
        }

        [BindProperty(SupportsGet = true)]
        public int Id { get; set; }

        public GroupModel Group { get; set; } = null!;
        public List<ContentModel> Contents { get; set; } = new();
        public List<TagModel> AvailableTags { get; set; } = new();
        public int TotalCount { get; set; }
        public int TotalPages { get; set; } = 1;
        public bool IsAdmin { get; set; }

        [BindProperty(SupportsGet = true, Name = "pageNumber")]
        public int CurrentPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public bool? FilterIsDeleted { get; set; }

        [BindProperty(SupportsGet = true)]
        public List<int> FilterTagIds { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? FilterTagMode { get; set; }

        public bool FilterTagMatchAll => TagFilterQuery.IsMatchAll(FilterTagMode);

        public Dictionary<string, string> FilterParams
        {
            get
            {
                var dict = new Dictionary<string, string> { ["id"] = Id.ToString() };
                if (FilterIsDeleted.HasValue) dict["FilterIsDeleted"] = FilterIsDeleted.Value.ToString().ToLower();
                TagFilterQuery.AddTo(dict, FilterTagIds, FilterTagMode, v => v);
                if (CurrentPage > 1) dict["pageNumber"] = CurrentPage.ToString();
                return dict;
            }
        }

        private int CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

        /// <summary>Link that filters the list by a single tag, keeping the other filters.</summary>
        public string? TagFilterUrl(int tagId)
        {
            var values = new RouteValueDictionary { ["id"] = Id, [$"{TagFilterQuery.TagIdsKey}[0]"] = tagId };
            if (FilterIsDeleted.HasValue) values["FilterIsDeleted"] = FilterIsDeleted.Value;
            return Url.Page("/GroupContent", values);
        }

        public string? PageUrl(int pageNumber)
        {
            var values = new RouteValueDictionary(FilterParams.ToDictionary(kv => kv.Key, kv => (object?)kv.Value))
            {
                ["pageNumber"] = pageNumber
            };
            return Url.Page("/GroupContent", values);
        }

        /// <summary>Loads the group if the current user may see it; otherwise returns the result to send.</summary>
        private async Task<IActionResult?> LoadGroupAsync()
        {
            IsAdmin = User.IsInRole("Admin");
            if (!User.HasClaim("GroupsEnabled", "true") && !IsAdmin)
            {
                return RedirectToPage("/Index");
            }

            var group = await _context.Groups.Include(g => g.Owner).FirstOrDefaultAsync(g => g.Id == Id);
            if (group == null) return NotFound();
            if (!IsAdmin && group.OwnerId != CurrentUserId) return Forbid();

            Group = group;
            return null;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var denied = await LoadGroupAsync();
            if (denied != null) return denied;

            var query = _context.Contents
                .Include(c => c.Owner)
                .Include(c => c.Tags)
                .Where(c => c.GroupId == Id);

            if (FilterIsDeleted.HasValue)
            {
                query = query.Where(c => c.IsDeleted == FilterIsDeleted.Value);
            }

            query = TagRules.ApplyTagFilter(query, FilterTagIds, FilterTagMatchAll);

            TotalCount = await query.CountAsync();
            TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
            if (CurrentPage < 1) CurrentPage = 1;
            if (CurrentPage > TotalPages) CurrentPage = TotalPages;

            Contents = await query
                .OrderByDescending(c => c.CreatedAt)
                .ThenByDescending(c => c.Id)
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            AvailableTags = await _tagsService.GetAvailableTagsAsync(CurrentUserId, IsAdmin);
            return Page();
        }

        public async Task<IActionResult> OnPostSetTagsAsync(int contentId, int[]? tagIds)
        {
            var denied = await LoadGroupAsync();
            if (denied != null) return denied;

            var result = await _tagsService.SetContentTagsAsync(contentId, tagIds ?? Array.Empty<int>(), CurrentUserId, IsAdmin);
            if (result == TagOperationResult.Success) TempData["ActionMessage"] = "Tags updated.";
            else TempData["ActionError"] = TagRules.ErrorMessage(result);

            return RedirectToPage(FilterParams.ToDictionary(kv => kv.Key, kv => (object)kv.Value));
        }

        public async Task<IActionResult> OnPostRemoveFromGroupAsync(int contentId)
        {
            var denied = await LoadGroupAsync();
            if (denied != null) return denied;

            var content = await _context.Contents.FirstOrDefaultAsync(c => c.Id == contentId && c.GroupId == Id);
            if (content == null)
            {
                TempData["ActionError"] = "Content not found in this group.";
            }
            else if (!IsAdmin && content.OwnerId != CurrentUserId)
            {
                TempData["ActionError"] = "You are not allowed to change this content.";
            }
            else
            {
                content.GroupId = null;
                content.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                TempData["ActionMessage"] = $"Content #{contentId} removed from the group.";
            }

            return RedirectToPage(FilterParams.ToDictionary(kv => kv.Key, kv => (object)kv.Value));
        }
    }
}
