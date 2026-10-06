using ContentCms.API.Models;
using ContentCms.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace ContentCms.API.Pages
{
    [Authorize(AuthenticationSchemes = "Cookies")]
    public class GroupsModel : PageModel
    {
        private readonly IGroupsService _groupsService;

        public GroupsModel(IGroupsService groupsService)
        {
            _groupsService = groupsService;
        }

        public List<GroupModel> Groups { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            if (!User.HasClaim("GroupsEnabled", "true") && !User.IsInRole("Admin"))
            {
                return RedirectToPage("/Index");
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdStr, out int userId))
            {
                if (User.IsInRole("Admin"))
                {
                    Groups = await _groupsService.GetAllGroupsAsync();
                }
                else
                {
                    Groups = await _groupsService.GetGroupsByOwnerIdAsync(userId);
                }
            }

            return Page();
        }

        public async Task<IActionResult> OnPostCreateAsync(string name, string? description)
        {
            if (string.IsNullOrWhiteSpace(name)) return RedirectToPage();

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdStr, out int userId))
            {
                var group = new GroupModel
                {
                    Name = name,
                    Description = description,
                    OwnerId = userId
                };
                await _groupsService.CreateGroupAsync(group);
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdateAsync(int id, string name, string? description)
        {
            await _groupsService.UpdateGroupAsync(id, name, description);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            await _groupsService.DeleteGroupAsync(id);
            return RedirectToPage();
        }

        // Bulk Actions

        public async Task<IActionResult> OnPostBulkToggleEnabledAsync(int id, bool enabled)
        {
            await _groupsService.BulkToggleEnabledAsync(id, enabled);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostBulkToggleVisibilityAsync(int id, bool isPublic)
        {
            await _groupsService.BulkToggleVisibilityAsync(id, isPublic);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostBulkToggleDeleteAsync(int id, bool isDeleted)
        {
            await _groupsService.BulkToggleDeleteAsync(id, isDeleted);
            return RedirectToPage();
        }
    }
}
