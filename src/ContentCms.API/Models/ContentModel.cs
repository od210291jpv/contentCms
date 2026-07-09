using System.ComponentModel.DataAnnotations;

namespace ContentCms.API.Models
{
    public class ContentModel
    {
        [Key]
        public int Id { get; set; }

        // Owner field - relation to user ID
        public int OwnerId { get; set; }

        // Navigation property to the owner
        public UserModel Owner { get; set; } = null!;

        // Enabled status - if false, content is disabled
        public bool Enabled { get; set; } = true;

        // Description - text field, up to 200 characters
        [MaxLength(200)]
        public string? Description { get; set; }

        // Path to the content location in CMS content directory
        [Required]
        public string Path { get; set; } = string.Empty;

        // IsPublic - if true, the link to the content should be accessible anonymously
        public bool IsPublic { get; set; } = false;

        // Soft delete flag
        public bool IsDeleted { get; set; } = false;

        // Timestamps
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
