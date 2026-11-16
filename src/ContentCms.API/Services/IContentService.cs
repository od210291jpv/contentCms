using ContentCms.API.Models;

namespace ContentCms.API.Services
{
    public interface IContentService
    {
        Task<IEnumerable<ContentModel>> GetAllAsync();
        Task<ContentModel?> GetByIdAsync(int id, int userId);

        /// <summary>Every content item of the owner, including disabled, private and soft-deleted ones, with tags loaded.</summary>
        Task<List<ContentModel>> GetOwnedWithRelationsAsync(int ownerId);
        Task<ContentModel> CreateAsync(ContentModel content);
        Task<bool> UpdateAsync(int id, ContentModel content);
        Task<bool> SoftDeleteAsync(int id);
        Task<bool> SetEnabledAsync(int id, bool enabled);
    }
}
