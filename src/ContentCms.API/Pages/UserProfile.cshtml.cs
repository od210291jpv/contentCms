using ContentCms.API.Models;
using ContentCms.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ContentCms.API.Pages
{
    [Authorize(AuthenticationSchemes = "Cookies")]
    public class UserProfileModel : PageModel
    {
        private readonly IUsersService _usersService;
        private readonly ContentCmsDbContext _context;

        public UserProfileModel(IUsersService usersService, ContentCmsDbContext context)
        {
            _usersService = usersService;
            _context = context;
        }

        public UserModel? ProfileUser { get; set; }

        // Dashboard stats
        public int TotalUploads { get; set; }
        public int TotalBlocked { get; set; }
        public string RequestsPerMonthJson { get; set; } = "[]";

        public async Task<IActionResult> OnGetAsync(int id)
        {
            if (!User.IsInRole("Admin"))
                return RedirectToPage("/Index");

            ProfileUser = await _usersService.GetUserByIdAsync(id);

            if (ProfileUser == null)
                return NotFound();

            // 1. Total uploaded contents for this user
            TotalUploads = await _context.Contents
                .Where(c => c.OwnerId == id)
                .CountAsync();

            // 2. Total blocked contents for this user (via ContentActionLogs)
            TotalBlocked = await _context.ContentActionLogs
                .Include(l => l.Content)
                .Where(l => l.Content.OwnerId == id && l.ActionType == ContentActionType.Blocked)
                .CountAsync();

            // 3. Request rate per month (last 12 months)
            var twelveMonthsAgo = DateTime.UtcNow.AddMonths(-11).Date;
            var firstOfMonth = new DateTime(twelveMonthsAgo.Year, twelveMonthsAgo.Month, 1);

            var requestLogs = await _context.ContentActionLogs
                .Include(l => l.Content)
                .Where(l => l.Content.OwnerId == id
                         && l.ActionType == ContentActionType.Requested
                         && l.Timestamp >= firstOfMonth)
                .ToListAsync();

            var requestsPerMonth = requestLogs
                .GroupBy(l => new { l.Timestamp.Year, l.Timestamp.Month })
                .Select(g => new
                {
                    month = $"{g.Key.Year}-{g.Key.Month:D2}",
                    count = g.Count()
                })
                .OrderBy(x => x.month)
                .ToList();

            RequestsPerMonthJson = JsonSerializer.Serialize(requestsPerMonth);

            return Page();
        }
    }
}
