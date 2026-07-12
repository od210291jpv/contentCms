using ContentCms.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Security.Claims;

namespace ContentCms.API.Pages
{
    [Authorize(AuthenticationSchemes = "Cookies")]
    public class IndexModel : PageModel
    {
        private readonly ContentCmsDbContext _context;

        public IndexModel(ContentCmsDbContext context)
        {
            _context = context;
        }

        public string UploadsPerDayJson { get; set; } = "[]";
        public string RemovesPerDayJson { get; set; } = "[]";
        public string RequestsPerDayJson { get; set; } = "[]";
        public string BlocksPerDayJson { get; set; } = "[]";
        public string UnblocksPerDayJson { get; set; } = "[]";
        public string UploadsPerUserJson { get; set; } = "[]";

        public async Task OnGetAsync()
        {
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

            bool isAdmin = User.IsInRole("Admin");
            string? userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int userId = userIdClaim != null ? int.Parse(userIdClaim) : 0;

            var logsQuery = _context.ContentActionLogs
                .Include(l => l.Content)
                .Where(l => l.Timestamp >= thirtyDaysAgo);

            if (!isAdmin)
            {
                logsQuery = logsQuery.Where(l => l.Content.OwnerId == userId);
            }

            var logs = await logsQuery.ToListAsync();

            // Group by Action Type and Date
            var groupedLogs = logs
                .GroupBy(l => new { l.ActionType, Date = l.Timestamp.Date })
                .Select(g => new
                {
                    ActionType = g.Key.ActionType,
                    Date = g.Key.Date.ToString("yyyy-MM-dd"),
                    Count = g.Count()
                })
                .ToList();

            // Helper function to extract series
            IEnumerable<object> ExtractSeries(ContentActionType type)
            {
                return groupedLogs
                    .Where(g => g.ActionType == type)
                    .OrderBy(g => g.Date)
                    .Select(g => new { date = g.Date, count = g.Count });
            }

            UploadsPerDayJson = JsonSerializer.Serialize(ExtractSeries(ContentActionType.Created));
            RemovesPerDayJson = JsonSerializer.Serialize(ExtractSeries(ContentActionType.Removed));
            RequestsPerDayJson = JsonSerializer.Serialize(ExtractSeries(ContentActionType.Requested));
            BlocksPerDayJson = JsonSerializer.Serialize(ExtractSeries(ContentActionType.Blocked));
            UnblocksPerDayJson = JsonSerializer.Serialize(ExtractSeries(ContentActionType.Unblocked));

            // Uploads per user (all time)
            var contentsQuery = _context.Contents
                .Include(c => c.Owner)
                .AsQueryable();

            if (!isAdmin)
            {
                contentsQuery = contentsQuery.Where(c => c.OwnerId == userId);
            }

            var userUploads = await contentsQuery
                .GroupBy(c => c.Owner.Username)
                .Select(g => new
                {
                    username = g.Key,
                    count = g.Count()
                })
                .ToListAsync();

            UploadsPerUserJson = JsonSerializer.Serialize(userUploads);
        }
    }
}
