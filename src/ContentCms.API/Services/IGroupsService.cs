using ContentCms.API.Models;

namespace ContentCms.API.Services
{
    public interface IGroupsService
    {
        Task<GroupModel> CreateGroupAsync(GroupModel group);
        Task<GroupModel?> GetGroupByIdAsync(int id);
        Task<List<GroupModel>> GetGroupsByOwnerIdAsync(int ownerId);
        Task<List<GroupModel>> GetAllGroupsAsync();
        Task<bool> UpdateGroupAsync(int id, string name, string? description);
        Task<bool> DeleteGroupAsync(int id);

        // Bulk operations
        Task<bool> BulkToggleEnabledAsync(int groupId, bool enabled);
        Task<bool> BulkToggleVisibilityAsync(int groupId, bool isPublic);
        Task<bool> BulkToggleDeleteAsync(int groupId, bool isDeleted);
    }
}
