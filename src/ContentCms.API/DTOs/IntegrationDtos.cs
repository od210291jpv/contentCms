namespace ContentCms.API.DTOs
{
    public class TagDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
    }

    public class GroupDto
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class UserListItemDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    /// <summary>One item of the <c>GET api/Content/sync</c> snapshot.</summary>
    public class ContentSyncItemDto
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public int? GroupId { get; set; }
        public bool Enabled { get; set; }
        public bool IsPublic { get; set; }
        public bool IsDeleted { get; set; }
        public string? Description { get; set; }
        public string Path { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<TagDto> Tags { get; set; } = new();
    }
}
