using ContentCms.API.Models;
using ContentCms.API.Services;

namespace ContentCms.API.DTOs
{
    // Contracts of the content management API used by the mobile app.

    public class ContentItemDto
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public int? GroupId { get; set; }
        public string? GroupName { get; set; }
        public string? Description { get; set; }
        public string Path { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public bool IsPublic { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<TagDto> Tags { get; set; } = new();

        public static ContentItemDto From(ContentModel c) => new()
        {
            Id = c.Id,
            OwnerId = c.OwnerId,
            OwnerName = c.Owner?.Username ?? string.Empty,
            GroupId = c.GroupId,
            GroupName = c.Group?.Name,
            Description = c.Description,
            Path = c.Path,
            Enabled = c.Enabled,
            IsPublic = c.IsPublic,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            Tags = c.Tags.OrderBy(t => t.Name).Select(t => new TagDto { Id = t.Id, Name = t.Name, Color = t.Color }).ToList()
        };
    }

    public class ContentPageDto
    {
        public List<ContentItemDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }

        public static ContentPageDto From(ContentPage page) => new()
        {
            Items = page.Items.Select(ContentItemDto.From).ToList(),
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize,
            TotalPages = Math.Max(1, (int)Math.Ceiling(page.TotalCount / (double)page.PageSize))
        };
    }

    public class GroupSummaryDto
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int ContentCount { get; set; }
    }

    public class TagUsageDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public int OwnerId { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public int ContentCount { get; set; }
    }

    public class SaveTagRequest
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>#RRGGBB; anything else falls back to the default grey.</summary>
        public string? Color { get; set; }
    }

    public class SetContentTagsRequest
    {
        public List<int> TagIds { get; set; } = new();
    }

    public class SetContentGroupRequest
    {
        /// <summary>Null removes the content from its group.</summary>
        public int? GroupId { get; set; }
    }
}
