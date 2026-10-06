using ContentCms.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentCms.API.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly ContentCmsDbContext _context;

        public AuditLogService(ContentCmsDbContext context)
        {
            _context = context;
        }

        public async Task<(IEnumerable<ContentActionLog> Logs, int TotalCount)> GetPagedAuditLogsAsync(int page, int pageSize)
        {
            var query = _context.ContentActionLogs.Include(l => l.Content);
            var totalCount = await query.CountAsync();
            var logs = await query
                .OrderByDescending(l => l.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (logs, totalCount);
        }
    }
}
