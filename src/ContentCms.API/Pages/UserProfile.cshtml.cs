using ContentCms.API.Models;
using ContentCms.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ContentCms.API.Pages
{
    [Authorize(AuthenticationSchemes = "Cookies")]
    public class UserProfileModel : PageModel
    {
        private readonly IUsersService _usersService;

        public UserProfileModel(IUsersService usersService)
        {
            _usersService = usersService;
        }

        public UserModel? ProfileUser { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!User.IsInRole("Admin"))
                return RedirectToPage("/Index");

            ProfileUser = await _usersService.GetUserByIdAsync(id);

            if (ProfileUser == null)
                return NotFound();

            return Page();
        }
    }
}
