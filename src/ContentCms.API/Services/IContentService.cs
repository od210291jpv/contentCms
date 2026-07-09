using ContentCms.API.Models;

namespace ContentCms.API.Services
{
    public interface IContentService
    {
        Task<IEnumerable<ContentModel>> GetAllAsync();
        Task<ContentModel?> GetByIdAsync(int id);
        Task<ContentModel> CreateAsync(ContentModel content);
        Task<bool> UpdateAsync(int id, ContentModel content);
        Task<bool> SoftDeleteAsync(int id);
        Task<bool> SetEnabledAsync(int id, bool enabled);
    }
}
