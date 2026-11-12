using System.ComponentModel.DataAnnotations;

namespace ContentCms.API.Models
{
    /// <summary>
    /// A label that can be attached to any number of content items. Tags are owned by a user
    /// (like groups): users manage and assign their own tags, admins can manage all tags.
    /// </summary>
    public class TagModel
    {
        public const int NameMaxLength = 50;
        public const string DefaultColor = "#6c757d";

        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(NameMaxLength)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Badge background color in #RRGGBB format.</summary>
        [Required]
        [MaxLength(7)]
        public string Color { get; set; } = DefaultColor;

        public int OwnerId { get; set; }
        public UserModel Owner { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<ContentModel> Contents { get; set; } = new List<ContentModel>();
    }
}
