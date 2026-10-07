using System.Security.Claims;
using ContentCms.API.Models;
using ContentCms.API.Services.Plugins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ContentCms.API.Controllers
{
    /// <summary>
    /// Used by the browser (cookie session) to obtain a short-lived scoped token for a UI plugin widget.
    /// The token lets the host page call the API on behalf of the widget as the current user.
    /// </summary>
    [Route("plugins")]
    [Authorize(AuthenticationSchemes = "Cookies")]
    [AutoValidateAntiforgeryToken]
    public class PluginUiController : Controller
    {
        private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(20);

        private readonly IPluginService _plugins;
        private readonly IPluginTokenService _tokens;

        public PluginUiController(IPluginService plugins, IPluginTokenService tokens)
        {
            _plugins = plugins;
            _tokens = tokens;
        }

        [HttpPost("{id:int}/ui-token")]
        public async Task<IActionResult> IssueToken(int id)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Unauthorized();

            var view = await _plugins.GetViewAsync(id, userId);
            if (view == null || !view.EffectiveEnabled || !view.Plugin.Kind.HasFlag(PluginKind.Ui))
                return NotFound();

            var token = _tokens.Issue(userId, id, TokenLifetime);
            return Json(new { token, expiresIn = (int)TokenLifetime.TotalSeconds });
        }
    }
}
