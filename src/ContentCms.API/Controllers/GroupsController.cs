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
    }
}
