using ContentCms.API.DTOs;
using ContentCms.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ContentCms.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequestDto loginRequest)
        {
            try
            {
                var token = _authService.Authenticate(loginRequest.Username, loginRequest.Password);
                
                // Decode the token to get user info for the response DTO
                // The token format is base64(userId:role)
                var decodedBytes = Convert.FromBase64String(token);
                var decodedString = System.Text.Encoding.UTF8.GetString(decodedBytes);
                var parts = decodedString.Split(':');
                int userId = int.Parse(parts[0]);

                var user = _authService.GetUserById(userId);
                if (user == null)
                                    return NotFound("User not found.");

                var response = new LoginResponseDto
                {
                    Token = token,
                    User = new UserInfoDto
                    {
                        Id = user.Id,
                        Username = user.Username,
                        Email = user.Email,
                        Role = user.Role.ToString()
                    }
                };

                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("logout")]
        [Authorize]
        public IActionResult Logout()
        {
            // Extract user ID from the token via a helper or by parsing the current user's claims
            // Since we are using a custom base64 token, we might need to parse it manually if not using standard JWT claims.
            // However, for this implementation, let's assume we can get it from the Auth service validation.
            
            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return Unauthorized("No token provided.");
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var userId = _authService.ValidateToken(token);

            if (userId == null)
            {
                return Unauthorized("Invalid token.");
            }

            _authService.Logout(userId.Value);
            return Ok("Logged out successfully.");
        }

        [HttpGet("user/{userId}")]
        [Authorize]
        public IActionResult GetUser(int userId)
        {
            // Verify if the requester is authorized to see this user (e.g., they are admin or it's themselves)
            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return Unauthorized("No token provided.");
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var requesterId = _authService.ValidateToken(token);

            if (requesterId == null)
            {
                return Unauthorized("Invalid token.");
            }

            // Check if requester is admin or the same user
            if (requesterId != userId && !_authService.IsAdmin(requesterId.Value))
            {
                return Forbid();
            }

            var user = _authService.GetUserById(userId);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            var response = new UserInfoDto
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                Role = user.Role.ToString()
            };

            return Ok(response);
        }
    }
}
