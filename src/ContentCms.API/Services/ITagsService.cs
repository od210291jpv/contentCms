using ContentCms.API.Models;

namespace ContentCms.API.Services
{
    public record TagUsage(TagModel Tag, int ContentCount);

    public enum TagOperationResult
    {
        Success,
        NotFound,
        Forbidden,
        InvalidName,
        DuplicateName
    }

    public interface ITagsService
    {
        /// <summary>Tags the user may assign / filter by: own tags, or all tags for admins. Ordered by name.</summary>
        Task<List<TagModel>> GetAvailableTagsAsync(int userId, bool isAdmin);

        /// <summary>Available tags with the number of (not deleted) content items they are attached to.</summary>
        Task<List<TagUsage>> GetTagsWithUsageAsync(int userId, bool isAdmin);

        Task<(TagOperationResult Result, TagModel? Tag)> CreateAsync(string name, string? color, int ownerId);
        Task<TagOperationResult> UpdateAsync(int id, string name, string? color, int userId, bool isAdmin);
        Task<TagOperationResult> DeleteAsync(int id, int userId, bool isAdmin);

        /// <summary>
        /// Replaces the tags of a content item with <paramref name="tagIds"/>, limited to the tags the user may manage.
        /// Tags the user cannot manage (e.g. added by an admin) are left untouched.
        /// </summary>
        Task<TagOperationResult> SetContentTagsAsync(int contentId, IEnumerable<int> tagIds, int userId, bool isAdmin);
    }
}
