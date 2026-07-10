using System.ComponentModel.DataAnnotations;

namespace ContentCms.API.DTOs
{
    public class CreateUserDto
    {
        [Required]
        [MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(255)]
        public string Email { get;set; } = string.Empty;

        [Required]
        public ContentCms.API.Models.UserRole Role { get; set; } = ContentCms.API.Models.UserRole.User;

        [Required]
        public string Password { get; set; } = string.Empty;
    }
}
