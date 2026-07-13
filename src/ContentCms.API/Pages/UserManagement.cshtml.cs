using ContentCms.API.Models;
using ContentCms.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ContentCms.API.Pages
{
    [Authorize(AuthenticationSchemes = "Cookies")]
    public class UserManagementModel : PageModel
    {
        private readonly IUsersService _usersService;

        public UserManagementModel(IUsersService usersService)
        {
            _usersService = usersService;
        }

        public List<UserModel> Users { get; set; } = new();
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public string? StatusMessage { get; set; }

        private const int PageSize = 10;

        public async Task<IActionResult> OnGetAsync(int pageNumber = 1)
        {
            if (!User.IsInRole("Admin"))
                return RedirectToPage("/Index");

            CurrentPage = pageNumber < 1 ? 1 : pageNumber;

            var (users, totalCount) = await _usersService.GetAllUsersPagedAsync(CurrentPage, PageSize);
            TotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
            if (TotalPages == 0) TotalPages = 1;

            Users = users;
            return Page();
        }

        public async Task<IActionResult> OnPostChangeUsernameAsync(int id, string username)
        {
            if (!User.IsInRole("Admin")) return Forbid();

            var existing = await _usersService.GetUserByIdAsync(id);
            if (existing == null) return RedirectToPage();

            var updated = new UserModel
            {
                Username = username,
                Email = existing.Email
            };

            await _usersService.UpdateUserAsync(id, updated);
            return RedirectToPage(new { pageNumber = CurrentPage });
        }

        public async Task<IActionResult> OnPostToggleActivationAsync(int id, bool isActive)
        {
            if (!User.IsInRole("Admin")) return Forbid();

            if (isActive)
                await _usersService.DeactivateUserAsync(id);
            else
                await _usersService.ActivateUserAsync(id);

            return RedirectToPage(new { pageNumber = CurrentPage });
        }

        public async Task<IActionResult> OnPostSetPasswordAsync(int id, string password)
        {
            if (!User.IsInRole("Admin")) return Forbid();

            await _usersService.SetPasswordAsync(id, password);
            return RedirectToPage(new { pageNumber = CurrentPage });
        }

        public async Task<IActionResult> OnPostSoftDeleteAsync(int id)
        {
            if (!User.IsInRole("Admin")) return Forbid();

            await _usersService.SoftDeleteUserAsync(id);
            return RedirectToPage(new { pageNumber = CurrentPage });
        }
    }
}
