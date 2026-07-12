using ContentCms.API.Models;
using ContentCms.API.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace ContentCms.API.Pages
{
    public class LoginModel : PageModel
    {
        private readonly IAuthService _authService;

        public LoginModel(IAuthService authService)
        {
            _authService = authService;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();

        public string ReturnUrl { get; set; } = "/";

        [TempData]
        public string ErrorMessage { get; set; } = "";

        public class InputModel
        {
            [Required]
            [Display(Name = "Email or Username")]
            public string Login { get; set; } = "";

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; } = "";
        }

        public async Task<IActionResult> OnGetAsync(string returnUrl = "/")
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            ReturnUrl = returnUrl;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = "/")
        {
            ReturnUrl = returnUrl;

            if (ModelState.IsValid)
            {
                try
                {
                    // Authenticate returns a base64 token if successful
                    var token = _authService.Authenticate(Input.Login, Input.Password);
                    
                    var userId = _authService.ValidateToken(token);
                    if (userId.HasValue)
                    {
                        var user = _authService.GetUserById(userId.Value);
                        if (user != null)
                        {
                            var claims = new List<Claim>
                            {
                                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                                new Claim(ClaimTypes.Name, user.Username),
                                new Claim(ClaimTypes.Email, user.Email),
                                new Claim(ClaimTypes.Role, user.Role.ToString())
                            };

                            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                            var authProperties = new AuthenticationProperties
                            {
                                // AllowRefresh = true,
                                // IsPersistent = true,
                                // ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10),
                            };

                            await HttpContext.SignInAsync(
                                CookieAuthenticationDefaults.AuthenticationScheme, 
                                new ClaimsPrincipal(claimsIdentity), 
                                authProperties);

                            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                }
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }
    }
}
