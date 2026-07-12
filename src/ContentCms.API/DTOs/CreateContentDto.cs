using ContentCms.API.Models;
using System.ComponentModel.DataAnnotations;

namespace ContentCms.API.DTOs
{
    public class CreateContentDto
    {
        // Owner field - relation to user ID
        public int OwnerId { get; set; }

        // Enabled status - if false, content is disabled
        public bool Enabled { get; set; } = true;

        // Description - text field, up to 200 characters
        [MaxLength(200)]
        public string Description { get; set; } = string.Empty;

        // Path to the content location in CMS content directory
        [Required]
        public IFormFile? File { get; set; }

        // IsPublic - if true, the link to the content should be accessible anonymously
        public bool IsPublic { get; set; } = false;

        // Soft delete flag
        public bool IsDeleted { get; set; } = false;
    }
}
