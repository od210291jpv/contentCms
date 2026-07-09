using System.Threading.Tasks;
using ContentCms.API.Models;

namespace ContentCms.API.Services
{
    public interface IUsersService
    {
        Task<UserModel> CreateUserAsync(UserModel user);
        Task<bool> ActivateUserAsync(int userId);
        Task<bool> DeactivateUserAsync(int userId);
        Task<bool> UpdateUserAsync(int userId, UserModel userUpdate);
        Task<bool> SoftDeleteUserAsync(int userId);
        Task<bool> UpdateUserRoleAsync(int userId, UserRole role);
        Task<UserModel?> GetUserByIdAsync(int userId);
    }
}
