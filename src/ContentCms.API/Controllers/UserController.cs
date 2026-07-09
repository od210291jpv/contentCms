using ContentCms.API.DTOs;
using ContentCms.API.Models;
using ContentCms.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ContentCms.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IUsersService _usersService;
        private readonly IAuthService _authService;

        public UserController(IUsersService usersService, IAuthService authService)
        {
            _usersService = usersService;
            _authService = authService;
        }

        private int? GetRequesterId()
        {
            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return null;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            return _authService.ValidateToken(token);
        }

        private bool IsAdmin()
        {
            var userId = GetRequesterId();
            return userId != null && _authService.IsAdmin(userId.Value);
        }

        /// <summary>
        /// Create a new user
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto createUserDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var user = new UserModel
            {
                Username = createUserDto.Username,
                Email = createUserDto.Email,
                Password = createUserDto.Password,
                Role = createUserDto.Role,
                IsActive = true,
                IsDeleted = false
            };

            var createdUser = await _usersService.CreateUserAsync(user);
            return CreatedAtAction(nameof(GetUser), new { id = createdUser.Id }, createdUser);
        }

        /// <summary>
        /// Activate a user
        /// </summary>
        [HttpPut("{id}/activate")]
        public async Task<IActionResult> ActivateUser(int id)
        {
            var isAdmin = IsAdmin();
            if (!isAdmin)
            {
                return Forbid();
            }

            var result = await _usersService.ActivateUserAsync(id);
            if (!result)
            {
                return NotFound(new { message = $"User with id {id} not found." });
            }

            return Ok(new { message = $"User with id {id} activated successfully." });
        }

        /// <summary>
        /// Deactivate a user
        /// </summary>
        [HttpPut("{id}/deactivate")]
        public async Task<IActionResult> DeactivateUser(int id)
        {
            var isAdmin = IsAdmin();
            if (!isAdmin)
            {
                return Forbid();
            }

            var result = await _usersService.DeactivateUserAsync(id);
            if (!result)
            {
                return NotFound(new { message = $"User with id {id} not found." });
            }

            return Ok(new { message = $"User with id {id} deactivated successfully." });
        }

        /// <summary>
        /// Get user by ID
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetUser(int id)
        {
            var requesterId = GetRequesterId();
            if (requesterId == null)
            {
                return Unauthorized(new { message = "Invalid or missing authentication token." });
            }

            // Users can only view their own profile unless they are admin
            if (requesterId.Value != id && !_authService.IsAdmin(requesterId.Value))
            {
                return Forbid();
            }

            var user = await _usersService.GetUserByIdAsync(id);
            if (user == null)
            {
                return NotFound(new { message = $"User with id {id} not found." });
            }

            // Exclude sensitive data from response
            var userResponse = new
            {
                user.Id,
                user.Username,
                user.Email,
                user.Role,
                user.IsActive,
                user.IsDeleted,
                user.CreatedAt
            };

            return Ok(userResponse);
        }

        /// <summary>
        /// Update user information
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto updateUserDto)
        {
            var requesterId = GetRequesterId();
            if (requesterId == null)
            {
                return Unauthorized(new { message = "Invalid or missing authentication token." });
            }

            // Users can only update their own profile unless they are admin
            if (requesterId.Value != id && !_authService.IsAdmin(requesterId.Value))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var user = new UserModel
            {
                Username = updateUserDto.Username,
                Email = updateUserDto.Email
            };

            var result = await _usersService.UpdateUserAsync(id, user);
            if (!result)
            {
                return NotFound(new { message = $"User with id {id} not found." });
            }

            return Ok(new { message = $"User with id {id} updated successfully." });
        }

        /// <summary>
        /// Soft delete a user
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> SoftDeleteUser(int id)
        {
            var isAdmin = IsAdmin();
            if (!isAdmin)
            {
                return Forbid();
            }

            var result = await _usersService.SoftDeleteUserAsync(id);
            if (!result)
            {
                return NotFound(new { message = $"User with id {id} not found." });
            }

            return Ok(new { message = $"User with id {id} soft deleted successfully." });
        }
    }
}
