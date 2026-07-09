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
            return Ok(contents);
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

            return Ok(content);
        }

        // POST: api/Content
        [HttpPost]
        public async Task<ActionResult<ContentModel>> Create([FromBody] ContentModel content)
        {
            if (string.IsNullOrEmpty(content.Path))
            {
                return BadRequest("Path is required.");
            }

            var createdContent = await _contentService.CreateAsync(content);
            return CreatedAtAction(nameof(GetById), new { id = createdContent.Id }, createdContent);
        }

        // PUT: api/Content/5
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] ContentModel content)
        {
            if (id != content.Id)
            {
                return BadRequest("ID mismatch.");
            }

            var result = await _contentService.UpdateAsync(id, content);
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

            var pagedContents = userContents.Skip((page - 1) * pageSize).Take(pageSize).ToList();

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
