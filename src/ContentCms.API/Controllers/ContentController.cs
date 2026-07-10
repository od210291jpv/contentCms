using ContentCms.API.DTOs;
using ContentCms.API.Models;
using ContentCms.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ContentCms.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContentController : ControllerBase
    {
        private readonly IContentService _contentService;
        private readonly IUsersService _usersService;

        public ContentController(IContentService contentService, IUsersService usersService)
        {
            _contentService = contentService;
            _usersService = usersService;
        }

        // GET: api/Content
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ContentModel>>> GetAll()
        {
            var contents = await _contentService.GetAllAsync();
            return Ok(contents.Select(c => new
            {
                Id = c.Id,
                CreatedAt = c.CreatedAt,
                Description = c.Description,
                Enabled = c.Enabled,
                IsDeleted = c.IsDeleted,
                IsPublic = c.IsPublic,
                OwnerId = c.OwnerId,
                Path = c.Path,
            }).ToList());
        }

        // GET: api/Content/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ContentModel>> GetById(int id)
        {
            var content = await _contentService.GetByIdAsync(id);

            if (content == null)
            {
                return NotFound();
            }

            return Ok(new
            {
                Id = content.Id,
                CreatedAt = content.CreatedAt,
                Description = content.Description,
                Enabled = content.Enabled,
                IsDeleted = content.IsDeleted,
                IsPublic = content.IsPublic,
                OwnerId = content.OwnerId,
                Path = content.Path,
            });
        }

        // POST: api/Content
        [HttpPost]
        public async Task<ActionResult<ContentModel>> Create([FromForm] ContentDto content)
        {
            var host = HttpContext.Request.Host.ToUriComponent();

            if (content.File == null || content.File.Length == 0)
                return NotFound("file not selected");

            var user = await _usersService.GetUserByIdAsync(content.OwnerId);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            string path = Path.Combine(
             Directory.GetCurrentDirectory(), "wwwroot/img",
             content.File.FileName);

            using (var stream = new FileStream(path, FileMode.Create))
            {
                await content.File.CopyToAsync(stream);
            }

            string fileUrl = $"{HttpContext.Request.Scheme}://{host}/img/{content.File.FileName}";

            ContentModel contentModel = new ContentModel()
            {
                CreatedAt = DateTime.Now,
                DeletedAt = null,
                Description = content.Description,
                Enabled = content.Enabled,
                IsDeleted = content.IsDeleted,
                IsPublic = content.IsPublic,
                OwnerId = content.OwnerId,
                Path = fileUrl,
                UpdatedAt = null
            };

            var createdContent = await _contentService.CreateAsync(contentModel);
            return Ok(new {
                Id = createdContent.Id,
                CreatedAt = createdContent.CreatedAt,
                Description = createdContent.Description,
                Enabled = createdContent.Enabled,
                IsDeleted = createdContent.IsDeleted,
                IsPublic = createdContent.IsPublic,
                OwnerId = createdContent.OwnerId,
                Path = createdContent.Path,
            });
        }

        // PUT: api/Content/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int contentId, [FromForm] ContentDto content)
        {
            ContentModel? existingContent = await _contentService.GetByIdAsync(contentId);
            if(existingContent is null)
            {
                return NotFound(contentId);
            }

            ContentModel updatedContent = new ContentModel
            {
                Id = contentId,
                OwnerId = content.OwnerId,
                Enabled = content.Enabled,
                Description = content.Description,
                IsPublic = content.IsPublic,
                IsDeleted = content.IsDeleted,
                Path = existingContent.Path, // Keep the existing path if no new file is provided
                CreatedAt = existingContent.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                DeletedAt = existingContent.DeletedAt
            };

            var result = await _contentService.UpdateAsync(contentId, updatedContent);
            if (!result)
            {
                return NotFound();
            }

            return NoContent();
        }

        // DELETE: api/Content/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _contentService.SoftDeleteAsync(id);
            if (!result)
            {
                return NotFound();
            }

            return NoContent();
        }

        // GET: api/Content/user/{userId}
        [HttpGet("user/{userId}")]
        public async Task<ActionResult<IEnumerable<ContentModel>>> GetUserContent(int userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            var user = await _usersService.GetUserByIdAsync(userId);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            var contents = await _contentService.GetAllAsync();
            var userContents = contents.Where(c => c.OwnerId == userId && !c.IsDeleted).ToList();

            var pagedContents = userContents.Skip((page - 1) * pageSize).Take(pageSize).Select(c => new
            {
                Id = c.Id,
                CreatedAt = c.CreatedAt,
                Description = c.Description,
                Enabled = c.Enabled,
                IsDeleted = c.IsDeleted,
                IsPublic = c.IsPublic,
                OwnerId = c.OwnerId,
                Path = c.Path,
            }).ToList();

            return Ok(new
            {
                TotalItems = userContents.Count,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)userContents.Count / pageSize),
                Items = pagedContents
            });
        }

        // PUT: api/Content/{id}/assign
        [HttpPut("{id}/assign")]
        public async Task<IActionResult> AssignOwner(int id, [FromBody] int newOwnerId)
        {
            var content = await _contentService.GetByIdAsync(id);
            if (content == null)
            {
                return NotFound();
            }

            var newUser = await _usersService.GetUserByIdAsync(newOwnerId);
            if (newUser == null)
            {
                return NotFound("New owner not found.");
            }

            content.OwnerId = newOwnerId;
            content.UpdatedAt = DateTime.UtcNow;

            await _contentService.UpdateAsync(id, content);

            return NoContent();
        }

        // PUT: api/Content/{id}/status
        [HttpPut("{id}/status")]
        public async Task<IActionResult> SetStatus(int id, [FromBody] bool enabled)
        {
            var result = await _contentService.SetEnabledAsync(id, enabled);
            if (!result)
            {
                return NotFound();
            }

            return NoContent();
        }
    }
}
