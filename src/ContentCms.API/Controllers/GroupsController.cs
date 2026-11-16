using ContentCms.API.DTOs;
using ContentCms.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContentCms.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GroupsController : ControllerBase
    {
        private readonly IGroupsService _groupsService;
        private readonly IAuthService _authService;

        public GroupsController(IGroupsService groupsService, IAuthService authService)
        {
            _groupsService = groupsService;
            _authService = authService;
        }

        // GET: api/Groups - the caller's own groups.
        [HttpGet]
        public async Task<ActionResult<List<GroupDto>>> GetMine([FromHeader(Name = "Authorization")] string? token)
        {
            int? userId = _authService.ValidateToken(AuthHeader.Token(token));
            if (userId is null) return Unauthorized();

            var groups = await _groupsService.GetGroupsByOwnerIdAsync(userId.Value);
            return Ok(groups
                .OrderBy(g => g.Name)
                .Select(g => new GroupDto { Id = g.Id, OwnerId = g.OwnerId, Name = g.Name, Description = g.Description })
                .ToList());
        }

        // GET: api/Groups/summary - groups the caller may manage (all groups for admins) with their content counts.
        [HttpGet("summary")]
        public async Task<ActionResult<List<GroupSummaryDto>>> Summary(
            [FromHeader(Name = "Authorization")] string? token, [FromServices] IContentBrowseService browse)
        {
            if (ApiCaller.From(_authService, token) is not { } caller) return Unauthorized();

            var groups = await browse.GetGroupsAsync(caller.UserId, caller.IsAdmin);
            return Ok(groups.Select(g => new GroupSummaryDto
            {
                Id = g.Group.Id,
                OwnerId = g.Group.OwnerId,
                OwnerName = g.Group.Owner?.Username ?? string.Empty,
                Name = g.Group.Name,
                Description = g.Group.Description,
                ContentCount = g.ContentCount
            }).ToList());
        }

        // GET: api/Groups/3/content?page=1&pageSize=20 - the group's content (not deleted), newest first.
        [HttpGet("{id:int}/content")]
        public async Task<ActionResult<ContentPageDto>> Content(
            [FromHeader(Name = "Authorization")] string? token,
            [FromServices] IContentBrowseService browse,
            int id,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (ApiCaller.From(_authService, token) is not { } caller) return Unauthorized();

            var access = await browse.CanAccessGroupAsync(id, caller.UserId, caller.IsAdmin);
            if (access != ContentAccessResult.Success) return ApiCaller.From(access, "Group not found.");

            // Admins see the whole group; users only their own items (a group holds only its owner's content anyway).
            var result = await browse.GetContentAsync(caller.UserId, caller.IsAdmin, new ContentQuery(id, Page: page, PageSize: pageSize));
            return Ok(ContentPageDto.From(result));
        }
    }
}
