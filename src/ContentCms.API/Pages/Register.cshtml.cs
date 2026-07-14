using ContentCms.API.Models;
using ContentCms.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace ContentCms.API.Pages
{
    public class RegisterModel : PageModel
    {
        private readonly IUsersService _usersService;

        public RegisterModel(IUsersService usersService)
        {
            _usersService = usersService;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();

        public class InputModel
        {
            [Required]
            [StringLength(100, MinimumLength = 3)]
            public string Username { get; set; } = "";

            [Required]
            [EmailAddress]
            public string Email { get; set; } = "";

            [Required]
            [DataType(DataType.Password)]
            [StringLength(100, MinimumLength = 6)]
            public string Password { get; set; } = "";

            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; } = "";
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var user = new UserModel
                    {
                        Username = Input.Username,
                        Email = Input.Email,
                        Password = Input.Password,
                        Role = UserRole.User,
                        IsActive = true,
                        IsDeleted = false
                    };

                    await _usersService.CreateUserAsync(user);

                    TempData["StatusMessage"] = "Successfully registered! Please log in.";
                    return RedirectToPage("/Login");
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError(string.Empty, "Registration failed: " + ex.Message);
                }
            }

            return Page();
        }
    }
}
