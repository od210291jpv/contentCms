using ContentCms.API.Models;
using System.ComponentModel.DataAnnotations;

namespace ContentCms.API.DTOs
{
    public class ContentObjectDto
    {
        public int Id { get; set; }

        public int OwnerId { get; set; }


        public bool Enabled { get; set; } = true;

        [MaxLength(200)]
        public string? Description { get; set; }

        [Required]
        public string Path { get; set; } = string.Empty;

        public bool IsPublic { get; set; } = false;

        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public DateTime? DeletedAt { get; set; }
    }
}
