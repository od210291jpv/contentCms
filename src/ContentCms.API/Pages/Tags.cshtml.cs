using ContentCms.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace ContentCms.API.Pages
{
    [Authorize(AuthenticationSchemes = "Cookies")]
    public class TagsModel : PageModel
    {
        private readonly ITagsService _tagsService;

        public TagsModel(ITagsService tagsService)
        {
            _tagsService = tagsService;
        }

        public List<TagUsage> Tags { get; set; } = new();
        public bool IsAdmin { get; set; }

        private int CurrentUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

        public async Task OnGetAsync()
        {
            IsAdmin = User.IsInRole("Admin");
            Tags = await _tagsService.GetTagsWithUsageAsync(CurrentUserId, IsAdmin);
        }

        public async Task<IActionResult> OnPostCreateAsync(string name, string? color)
        {
            var (result, tag) = await _tagsService.CreateAsync(name, color, CurrentUserId);
            Report(result, $"Tag '{tag?.Name}' created.");
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdateAsync(int id, string name, string? color)
        {
            var result = await _tagsService.UpdateAsync(id, name, color, CurrentUserId, User.IsInRole("Admin"));
            Report(result, "Tag updated.");
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var result = await _tagsService.DeleteAsync(id, CurrentUserId, User.IsInRole("Admin"));
            Report(result, "Tag deleted.");
            return RedirectToPage();
        }

        private void Report(TagOperationResult result, string successMessage)
        {
            if (result == TagOperationResult.Success) TempData["ActionMessage"] = successMessage;
            else TempData["ActionError"] = TagRules.ErrorMessage(result);
        }
    }
}
