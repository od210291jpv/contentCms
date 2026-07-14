using ContentCms.API.Models;
using ContentCms.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ContentCms.API.Pages
{
    [Authorize(AuthenticationSchemes = "Cookies")]
    public class ContentManagementModel : PageModel
    {
        private readonly ContentCmsDbContext _context;
        private readonly IContentService _contentService;

        public ContentManagementModel(ContentCmsDbContext context, IContentService contentService)
        {
            _context = context;
            _contentService = contentService;
        }

        public List<ContentModel> Contents { get; set; } = new();
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public bool IsAdmin { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool? FilterEnabled { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool? FilterIsPublic { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool? FilterIsDeleted { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SortBy { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool SortDescending { get; set; }

        public Dictionary<string, string> FilterParams => new Dictionary<string, string>
        {
            { "FilterEnabled", FilterEnabled.HasValue ? FilterEnabled.ToString()! : "" },
            { "FilterIsPublic", FilterIsPublic.HasValue ? FilterIsPublic.ToString()! : "" },
            { "FilterIsDeleted", FilterIsDeleted.HasValue ? FilterIsDeleted.ToString()! : "" },
            { "SortBy", SortBy ?? "" },
            { "SortDescending", SortDescending.ToString() }
        };

        public async Task OnGetAsync(int pageNumber = 1)
        {
            CurrentPage = pageNumber < 1 ? 1 : pageNumber;
            int pageSize = 10;
            IsAdmin = User.IsInRole("Admin");
            string? userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int userId = userIdClaim != null ? int.Parse(userIdClaim) : 0;

            var query = _context.Contents.Include(c => c.Owner).AsQueryable();

            if (!IsAdmin)
            {
                query = query.Where(c => c.OwnerId == userId);
            }

            // Apply Filters
            if (FilterEnabled.HasValue)
            {
                query = query.Where(c => c.Enabled == FilterEnabled.Value);
            }

            if (FilterIsPublic.HasValue)
            {
                query = query.Where(c => c.IsPublic == FilterIsPublic.Value);
            }

            if (FilterIsDeleted.HasValue)
            {
                query = query.Where(c => c.IsDeleted == FilterIsDeleted.Value);
            }

            // Apply Sorting
            switch (SortBy)
            {
                case "ID":
                    query = SortDescending ? query.OrderByDescending(c => c.Id) : query.OrderBy(c => c.Id);
                    break;
                case "OwnerId":
                    query = SortDescending ? query.OrderByDescending(c => c.OwnerId) : query.OrderBy(c => c.OwnerId);
                    break;
                case "Description":
                    query = SortDescending ? query.OrderByDescending(c => c.Description) : query.OrderBy(c => c.Description);
                    break;
                case "CreatedAt":
                    query = SortDescending ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt);
                    break;
                case "UpdatedAt":
                    query = SortDescending ? query.OrderByDescending(c => c.UpdatedAt) : query.OrderBy(c => c.UpdatedAt);
                    break;
                case "DeletedAt":
                    query = SortDescending ? query.OrderByDescending(c => c.DeletedAt) : query.OrderBy(c => c.DeletedAt);
                    break;
                default:
                    // Default sort
                    query = query.OrderByDescending(c => c.CreatedAt);
                    break;
            }

            int totalCount = await query.CountAsync();
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            if(TotalPages == 0) TotalPages = 1;

            Contents = await query
                .Skip((CurrentPage - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostUploadAsync(IFormFile file, string description, bool isPublic)
        {
            if (file == null || file.Length == 0)
            {
                return RedirectToPage(new { error = "File is required." });
            }

            string? userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int userId = userIdClaim != null ? int.Parse(userIdClaim) : 0;

            var host = HttpContext.Request.Host.ToUriComponent();
            string imgDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "img");
            if (!Directory.Exists(imgDir)) Directory.CreateDirectory(imgDir);

            // Generate unique filename to avoid collisions
            string fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
            string path = Path.Combine(imgDir, fileName);

            using (var stream = new FileStream(path, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            string fileUrl = $"{HttpContext.Request.Scheme}://{host}/img/{fileName}";

            ContentModel contentModel = new ContentModel()
            {
                Description = description,
                IsPublic = isPublic,
                OwnerId = userId,
                Path = fileUrl,
                Enabled = true
            };

            await _contentService.CreateAsync(contentModel);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostToggleStatusAsync(int id, bool enabled)
        {
            await _contentService.SetEnabledAsync(id, enabled);
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostToggleVisibilityAsync(int id, bool isPublic)
        {
            var content = await _context.Contents.FindAsync(id);
            if (content != null)
            {
                content.IsPublic = isPublic;
                content.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdateDescriptionAsync(int id, string description)
        {
            var content = await _context.Contents.FindAsync(id);
            if (content != null)
            {
                content.Description = description;
                content.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostReassignAsync(int id, int newOwnerId)
        {
            if (!User.IsInRole("Admin")) return Forbid();

            var content = await _context.Contents.FindAsync(id);
            if (content != null)
            {
                content.OwnerId = newOwnerId;
                content.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSoftDeleteAsync(int id)
        {
            await _contentService.SoftDeleteAsync(id);
            return RedirectToPage();
        }
    }
}
