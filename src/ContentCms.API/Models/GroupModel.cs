using System.ComponentModel.DataAnnotations;

namespace ContentCms.API.Models
{
    public class GroupModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? Description { get; set; }

        public int OwnerId { get; set; }
        public UserModel Owner { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<ContentModel> Contents { get; set; } = new List<ContentModel>();
    }
}
