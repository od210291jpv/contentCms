using ContentCms.API.DTOs;
using ContentCms.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContentCms.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TagsController : ControllerBase
    {
        private readonly ITagsService _tagsService;
        private readonly IAuthService _authService;

        public TagsController(ITagsService tagsService, IAuthService authService)
        {
            _tagsService = tagsService;
            _authService = authService;
        }

        // GET: api/Tags - the tags the caller may assign (own tags; all tags for admins).
        [HttpGet]
        public async Task<ActionResult<List<TagDto>>> GetAvailable([FromHeader(Name = "Authorization")] string? token)
        {
            int? userId = _authService.ValidateToken(AuthHeader.Token(token));
            if (userId is null) return Unauthorized();

            var tags = await _tagsService.GetAvailableTagsAsync(userId.Value, _authService.IsAdmin(userId.Value));
            return Ok(tags.Select(t => new TagDto { Id = t.Id, Name = t.Name, Color = t.Color }).ToList());
        }

        // GET: api/Tags/usage - available tags with owner and the number of content items using them.
        [HttpGet("usage")]
        public async Task<ActionResult<List<TagUsageDto>>> Usage([FromHeader(Name = "Authorization")] string? token)
        {
            if (ApiCaller.From(_authService, token) is not { } caller) return Unauthorized();

            var tags = await _tagsService.GetTagsWithUsageAsync(caller.UserId, caller.IsAdmin);
            return Ok(tags.Select(u => new TagUsageDto
            {
                Id = u.Tag.Id,
                Name = u.Tag.Name,
                Color = u.Tag.Color,
                OwnerId = u.Tag.OwnerId,
                OwnerName = u.Tag.Owner?.Username ?? string.Empty,
                ContentCount = u.ContentCount
            }).ToList());
        }

        // POST: api/Tags  { "name": "cats", "color": "#ff8800" } - creates a tag owned by the caller.
        [HttpPost]
        public async Task<ActionResult<TagDto>> Create([FromHeader(Name = "Authorization")] string? token, [FromBody] SaveTagRequest request)
        {
            if (ApiCaller.From(_authService, token) is not { } caller) return Unauthorized();

            var (result, tag) = await _tagsService.CreateAsync(request.Name, request.Color, caller.UserId);
            if (result != TagOperationResult.Success) return ApiCaller.From(result);
            return StatusCode(StatusCodes.Status201Created, new TagDto { Id = tag!.Id, Name = tag.Name, Color = tag.Color });
        }

        // PUT: api/Tags/5  { "name": "kittens", "color": "#ff8800" } - renames / recolors a tag.
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update([FromHeader(Name = "Authorization")] string? token, int id, [FromBody] SaveTagRequest request)
        {
            if (ApiCaller.From(_authService, token) is not { } caller) return Unauthorized();

            var result = await _tagsService.UpdateAsync(id, request.Name, request.Color, caller.UserId, caller.IsAdmin);
            return result == TagOperationResult.Success ? NoContent() : ApiCaller.From(result);
        }

        // DELETE: api/Tags/5 - deletes the tag and removes it from all content.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(
            [FromHeader(Name = "Authorization")] string? token, [FromServices] IContentEventPublisher events, [FromServices] Models.ContentCmsDbContext db, int id)
        {
            if (ApiCaller.From(_authService, token) is not { } caller) return Unauthorized();

            // Content that loses the tag, so consumers (e.g. UnoGame) hear about it.
            var affected = db.Contents.Where(c => c.Tags.Any(t => t.Id == id)).Select(c => c.Id).ToList();

            var result = await _tagsService.DeleteAsync(id, caller.UserId, caller.IsAdmin);
            if (result != TagOperationResult.Success) return ApiCaller.From(result);

            foreach (var contentId in affected) await events.PublishAsync(contentId, DTOs.Events.ContentEventType.TagsChanged);
            return NoContent();
        }
    }
}
