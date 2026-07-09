using System.ComponentModel.DataAnnotations;

namespace ContentCms.API.Models
{
    public enum UserRole
    {
        Admin,
        User
    }

    public class UserModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        [Required]
        public UserRole Role { get; set; } = UserRole.User;

        public bool IsActive { get; set; } = true;

        public bool IsDeleted { get; set; } = false;

        // Navigation property: Content owned by this user
        public ICollection<ContentModel> OwnedContent { get; set; } = new List<ContentModel>();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
