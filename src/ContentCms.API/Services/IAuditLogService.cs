using ContentCms.API.Models;

namespace ContentCms.API.Services
{
    public interface IAuditLogService
    {
        Task<(IEnumerable<ContentActionLog> Logs, int TotalCount)> GetPagedAuditLogsAsync(int page, int pageSize);
    }
}
