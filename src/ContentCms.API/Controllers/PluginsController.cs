using System.Text.Json.Nodes;
using ContentCms.API.DTOs;
using ContentCms.API.Models;
using ContentCms.API.Services;
using ContentCms.API.Services.Plugins;
using Microsoft.AspNetCore.Mvc;

namespace ContentCms.API.Controllers
{
    /// <summary>
    /// Plugin management API. Only admins can create, update, delete plugins or switch them per user.
    /// Regular users can read plugins and configure the ones available to them.
    /// Plugin tokens are never accepted here: plugins cannot manage plugins.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PluginsController : ControllerBase
    {
        private readonly IPluginService _plugins;
        private readonly IUsersService _usersService;
        private readonly IAuthService _authService;
        private readonly IPluginTokenService _pluginTokens;

        public PluginsController(
            IPluginService plugins,
            IUsersService usersService,
            IAuthService authService,
            IPluginTokenService pluginTokens)
        {
            _plugins = plugins;
            _usersService = usersService;
            _authService = authService;
            _pluginTokens = pluginTokens;
        }

        // GET: api/plugins - plugins visible to the caller
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PluginDto>>> GetAll([FromHeader(Name = "Authorization")] string? token = null)
        {
            var (caller, error) = await GetCallerAsync(token);
            if (caller == null) return error!;

            var isAdmin = caller.Role == UserRole.Admin;
            var views = await _plugins.GetViewsForUserAsync(caller.Id, includeGloballyDisabled: isAdmin);
            return Ok(views.Select(v => PluginDto.From(v, includeSource: isAdmin)));
        }

        // GET: api/plugins/user/5 - plugins as seen by a given user (self or admin)
        [HttpGet("user/{userId}")]
        public async Task<ActionResult<IEnumerable<PluginDto>>> GetForUser(int userId, [FromHeader(Name = "Authorization")] string? token = null)
        {
            var (caller, error) = await GetCallerAsync(token);
            if (caller == null) return error!;
            if (!CanActOn(caller, userId)) return Forbid();

            var isAdmin = caller.Role == UserRole.Admin;
            var views = await _plugins.GetViewsForUserAsync(userId, includeGloballyDisabled: isAdmin);
            return Ok(views.Select(v => PluginDto.From(v, includeSource: isAdmin)));
        }

        // GET: api/plugins/3
        [HttpGet("{id}")]
        public async Task<ActionResult<PluginDto>> GetById(int id, [FromHeader(Name = "Authorization")] string? token = null)
        {
            var (caller, error) = await GetCallerAsync(token);
            if (caller == null) return error!;

            var isAdmin = caller.Role == UserRole.Admin;
            var view = await _plugins.GetViewAsync(id, caller.Id);
            if (view == null || (!isAdmin && !view.Plugin.IsEnabled)) return NotFound();
            return Ok(PluginDto.From(view, includeSource: isAdmin));
        }

        // POST: api/plugins
        [HttpPost]
        public async Task<ActionResult<PluginDto>> Create([FromBody] PluginInputDto dto, [FromHeader(Name = "Authorization")] string? token = null)
        {
            var (caller, error) = await GetCallerAsync(token);
            if (caller == null) return error!;
            if (caller.Role != UserRole.Admin) return Forbid();

            var (plugin, errors) = await _plugins.CreateAsync(dto.ToInput(), caller.Id);
            if (plugin == null) return BadRequest(new { errors });

            var view = await _plugins.GetViewAsync(plugin.Id, caller.Id);
            return CreatedAtAction(nameof(GetById), new { id = plugin.Id }, PluginDto.From(view!, includeSource: true));
        }

        // PUT: api/plugins/3
        [HttpPut("{id}")]
        public async Task<ActionResult<PluginDto>> Update(int id, [FromBody] PluginInputDto dto, [FromHeader(Name = "Authorization")] string? token = null)
        {
            var (caller, error) = await GetCallerAsync(token);
            if (caller == null) return error!;
            if (caller.Role != UserRole.Admin) return Forbid();

            var (plugin, errors) = await _plugins.UpdateAsync(id, dto.ToInput());
            if (plugin == null)
                return errors.Count == 0 ? NotFound() : BadRequest(new { errors });

            var view = await _plugins.GetViewAsync(plugin.Id, caller.Id);
            return Ok(PluginDto.From(view!, includeSource: true));
        }

        // DELETE: api/plugins/3
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id, [FromHeader(Name = "Authorization")] string? token = null)
        {
            var (caller, error) = await GetCallerAsync(token);
            if (caller == null) return error!;
            if (caller.Role != UserRole.Admin) return Forbid();

            return await _plugins.DeleteAsync(id) ? NoContent() : NotFound();
        }

        // PUT: api/plugins/3/users/5/enabled   body: true|false   (admin only)
        [HttpPut("{id}/users/{userId}/enabled")]
        public async Task<IActionResult> SetUserEnabled(int id, int userId, [FromBody] bool enabled, [FromHeader(Name = "Authorization")] string? token = null)
        {
            var (caller, error) = await GetCallerAsync(token);
            if (caller == null) return error!;
            if (caller.Role != UserRole.Admin) return Forbid();

            return await _plugins.SetUserEnabledAsync(id, userId, enabled) ? NoContent() : NotFound();
        }

        // GET: api/plugins/3/users/5/config
        [HttpGet("{id}/users/{userId}/config")]
        public async Task<ActionResult<JsonObject>> GetUserConfig(int id, int userId, [FromHeader(Name = "Authorization")] string? token = null)
        {
            var (caller, error) = await GetCallerAsync(token);
            if (caller == null) return error!;
            if (!CanActOn(caller, userId)) return Forbid();

            var view = await _plugins.GetViewAsync(id, userId);
            if (view == null || (caller.Role != UserRole.Admin && !view.Plugin.IsEnabled)) return NotFound();
            return Ok(view.Config);
        }

        // PUT: api/plugins/3/users/5/config   body: { "field": value, ... }
        [HttpPut("{id}/users/{userId}/config")]
        public async Task<IActionResult> SaveUserConfig(int id, int userId, [FromBody] JsonObject config, [FromHeader(Name = "Authorization")] string? token = null)
        {
            var (caller, error) = await GetCallerAsync(token);
            if (caller == null) return error!;
            if (!CanActOn(caller, userId)) return Forbid();

            var view = await _plugins.GetViewAsync(id, userId);
            if (view == null || (caller.Role != UserRole.Admin && !view.Plugin.IsEnabled)) return NotFound();

            var (ok, errors) = await _plugins.SaveUserConfigAsync(id, userId, config);
            return ok ? NoContent() : BadRequest(new { errors });
        }

        // GET: api/plugins/3/logs?take=50   (admin only)
        [HttpGet("{id}/logs")]
        public async Task<ActionResult<IEnumerable<PluginLogDto>>> GetLogs(int id, [FromQuery] int take = 50, [FromHeader(Name = "Authorization")] string? token = null)
        {
            var (caller, error) = await GetCallerAsync(token);
            if (caller == null) return error!;
            if (caller.Role != UserRole.Admin) return Forbid();

            if (await _plugins.GetAsync(id) == null) return NotFound();
            var logs = await _plugins.GetLogsAsync(id, take);
            return Ok(logs.Select(l => new PluginLogDto
            {
                Id = l.Id,
                Timestamp = l.Timestamp,
                Level = l.Level,
                Trigger = l.Trigger,
                UserId = l.UserId,
                Message = l.Message
            }));
        }

        private static bool CanActOn(UserModel caller, int targetUserId) =>
            caller.Role == UserRole.Admin || caller.Id == targetUserId;

        private async Task<(UserModel? Caller, ActionResult? Error)> GetCallerAsync(string? token)
        {
            // Scoped plugin tokens are deliberately rejected on this controller.
            if (_pluginTokens.Validate(token) != null)
                return (null, Forbid());

            var userId = token == null ? null : _authService.ValidateToken(token);
            if (userId == null)
                return (null, Unauthorized());

            var user = await _usersService.GetUserByIdAsync(userId.Value);
            if (user == null || !user.IsActive || user.IsDeleted)
                return (null, Unauthorized());

            return (user, null);
        }
    }
}
