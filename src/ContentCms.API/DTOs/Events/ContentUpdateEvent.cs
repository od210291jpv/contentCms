namespace ContentCms.API.DTOs.Events
{
    public enum ContentEventType
    {
        Created,
        Deleted,
        Reassigned,
        Enabled,
        Disabled,
        Edited,
        MadePrivate,
        MadePublic
    }

    public class ContentUpdateEvent
    {
        public ContentEventType EventType { get; set; }
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public bool Enabled { get; set; }
        public string? Description { get; set; }
        public string Path { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
    }
}
