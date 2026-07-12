using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContentCms.API.Models
{
    public enum ContentActionType
    {
        Created,
        Removed,
        Requested,
        Blocked,
        Unblocked
    }

    public class ContentActionLog
    {
        [Key]
        public int Id { get; set; }

        public int ContentId { get; set; }

        public ContentActionType ActionType { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [ForeignKey("ContentId")]
        public ContentModel Content { get; set; } = null!;
    }
}
