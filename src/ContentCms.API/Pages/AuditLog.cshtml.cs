using ContentCms.API.Models;
using ContentCms.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ContentCms.API.Pages
{
    [Authorize(AuthenticationSchemes = "Cookies", Roles = "Admin")]
    public class AuditLogModel : PageModel
    {
        private readonly IAuditLogService _auditLogService;

        public AuditLogModel(IAuditLogService auditLogService)
        {
            _auditLogService = auditLogService;
        }

        public List<ContentActionLog> Logs { get; set; } = new();
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;

        private const int PageSize = 10;

        public async Task<IActionResult> OnGetAsync(int pageNumber = 1)
        {
            CurrentPage = pageNumber < 1 ? 1 : pageNumber;

            var (logs, totalCount) = await _auditLogService.GetPagedAuditLogsAsync(CurrentPage, PageSize);
            TotalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
            if (TotalPages == 0) TotalPages = 1;

            Logs = logs.ToList();
            return Page();
        }

        public string GetReadableActionType(ContentActionType actionType)
        {
            return actionType switch
            {
                ContentActionType.Created => "Content Created",
                ContentActionType.Removed => "Content Removed",
                ContentActionType.Requested => "Content Requested",
                ContentActionType.Blocked => "Content Blocked",
                ContentActionType.Unblocked => "Content Unblocked",
                _ => actionType.ToString()
            };
        }
    }
}
