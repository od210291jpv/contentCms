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
    }
}
