using ContentCms.API.Models;
using ContentCms.API.Services;
using ContentCms.API.Services.Plugins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ContentCms.API.Pages
{
    /// <summary>Posted plugin definition from the admin create/edit modal.</summary>
    public class PluginFormInput
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Version { get; set; } = "1.0.0";
        public bool KindBackend { get; set; } = true;
        public bool KindUi { get; set; }
        public bool KindContentAction { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool EnabledByDefault { get; set; }
        public string? Script { get; set; }
        public List<string> Events { get; set; } = new();
        public PluginEventScope EventScope { get; set; } = PluginEventScope.Owner;
        public string? UiHtml { get; set; }
        public List<string> Slots { get; set; } = new();
        public string? ConfigSchemaJson { get; set; }

        public PluginInput ToInput() => new()
        {
            Key = Key ?? string.Empty,
            Name = Name ?? string.Empty,
            Description = Description,
            Version = Version ?? string.Empty,
            Kind = (KindBackend ? PluginKind.Backend : PluginKind.None) | (KindUi ? PluginKind.Ui : PluginKind.None) | (KindContentAction ? PluginKind.ContentAction : PluginKind.None),
            IsEnabled = IsEnabled,
            EnabledByDefault = EnabledByDefault,
            Script = Script,
            SubscribedEvents = string.Join(",", Events ?? new List<string>()),
            EventScope = EventScope,
            UiHtml = UiHtml,
            UiSlots = string.Join(",", Slots ?? new List<string>()),
            ConfigSchemaJson = ConfigSchemaJson
        };
    }

    /// <summary>Everything the plugins panel partial needs to render.</summary>
    public class PluginsPanelModel
    {
        public int ProfileUserId { get; init; }
        public string ProfileUsername { get; init; } = string.Empty;
        public bool IsAdmin { get; init; }
        public bool IsSelf { get; init; }
        public List<PluginView> Views { get; init; } = new();
        public PluginFormInput? FormState { get; init; }
        public List<string> FormErrors { get; init; } = new();
        public string? Message { get; init; }
        public string? Error { get; init; }
    }

    [Authorize(AuthenticationSchemes = "Cookies")]
    public class UserProfileModel : PageModel
    {
        private readonly IUsersService _usersService;
        private readonly ContentCmsDbContext _context;
        private readonly IPluginService _plugins;

        public UserProfileModel(IUsersService usersService, ContentCmsDbContext context, IPluginService plugins)
        {
            _usersService = usersService;
            _context = context;
            _plugins = plugins;
        }

        public UserModel? ProfileUser { get; set; }

        // Dashboard stats
        public int TotalUploads { get; set; }
        public int TotalBlocked { get; set; }
        public string RequestsPerMonthJson { get; set; } = "[]";

        // Plugins
        [BindProperty]
        public PluginFormInput PluginForm { get; set; } = new();

        public PluginsPanelModel PluginsPanel { get; private set; } = new();
        public PluginWidgetsModel ProfileWidgets { get; private set; } = new() { Slot = "profile" };

        [TempData]
        public string? PluginMessage { get; set; }

        [TempData]
        public string? PluginError { get; set; }

        private bool IsAdmin => User.IsInRole("Admin");

        private int CurrentUserId =>
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

        /// <summary>Admins may open any profile; everybody else only their own.</summary>
        private bool CanAccess(int profileId) => IsAdmin || profileId == CurrentUserId;

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            var profileId = id ?? CurrentUserId;

            if (!CanAccess(profileId))
                return RedirectToPage("/Index");

            if (!await LoadAsync(profileId))
                return NotFound();

            return Page();
        }

        // ---------- plugin handlers ----------

        /// <summary>Create or update a plugin definition (admin only).</summary>
        public async Task<IActionResult> OnPostSavePluginAsync(int profileId)
        {
            if (!IsAdmin) return Forbid();

            List<string> errors;
            if (PluginForm.Id == 0)
            {
                (_, errors) = await _plugins.CreateAsync(PluginForm.ToInput(), CurrentUserId);
            }
            else
            {
                var (plugin, updateErrors) = await _plugins.UpdateAsync(PluginForm.Id, PluginForm.ToInput());
                errors = updateErrors;
                if (plugin == null && errors.Count == 0) return NotFound();
            }

            if (errors.Count > 0)
            {
                // Redisplay the page with the user's input so a long script is not lost.
                if (!await LoadAsync(profileId, formState: PluginForm, formErrors: errors))
                    return NotFound();
                return Page();
            }

            PluginMessage = PluginForm.Id == 0 ? "Plugin created." : "Plugin updated.";
            return RedirectToPlugins(profileId);
        }

        /// <summary>Delete a plugin definition together with its assignments, storage and logs (admin only).</summary>
        public async Task<IActionResult> OnPostDeletePluginAsync(int profileId, int pluginId)
        {
            if (!IsAdmin) return Forbid();

            if (await _plugins.DeleteAsync(pluginId))
                PluginMessage = "Plugin deleted.";
            else
                PluginError = "Plugin not found.";
            return RedirectToPlugins(profileId);
        }

        /// <summary>Enable or disable a plugin for one user (admin only).</summary>
        public async Task<IActionResult> OnPostToggleAssignmentAsync(int profileId, int pluginId, bool enabled)
        {
            if (!IsAdmin) return Forbid();

            if (await _plugins.SetUserEnabledAsync(pluginId, profileId, enabled))
                PluginMessage = enabled ? "Plugin enabled for this user." : "Plugin disabled for this user.";
            else
                PluginError = "Plugin or user not found.";
            return RedirectToPlugins(profileId);
        }

        /// <summary>Save the user's configuration values (self or admin).</summary>
        public async Task<IActionResult> OnPostSaveConfigAsync(int profileId, int pluginId)
        {
            if (!CanAccess(profileId)) return Forbid();

            var view = await _plugins.GetViewAsync(pluginId, profileId);
            if (view == null) return NotFound();

            // Regular users can only configure plugins that are active for them.
            if (!IsAdmin && !view.EffectiveEnabled) return Forbid();

            var input = new JsonObject();
            foreach (var field in view.Fields)
            {
                var formKey = "cfg_" + field.Key;
                if (field.Type == "boolean")
                    input[field.Key] = Request.Form.ContainsKey(formKey) ? "true" : "false";
                else if (Request.Form.TryGetValue(formKey, out var value))
                    input[field.Key] = value.ToString();
            }

            var (ok, errors) = await _plugins.SaveUserConfigAsync(pluginId, profileId, input);
            if (ok)
                PluginMessage = $"Configuration of '{view.Plugin.Name}' saved.";
            else
                PluginError = $"Configuration of '{view.Plugin.Name}' was not saved: {string.Join(" ", errors)}";
            return RedirectToPlugins(profileId);
        }

        /// <summary>Latest log entries of a plugin as JSON (admin only).</summary>
        public async Task<IActionResult> OnGetPluginLogsAsync(int pluginId)
        {
            if (!IsAdmin) return Forbid();

            var logs = await _plugins.GetLogsAsync(pluginId, 100);
            return new JsonResult(logs.Select(l => new
            {
                time = l.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") + " UTC",
                level = l.Level,
                trigger = l.Trigger,
                userId = l.UserId,
                message = l.Message
            }));
        }

        private IActionResult RedirectToPlugins(int profileId) =>
            RedirectToPage(null, null, new { id = profileId }, "plugins");

        // ---------- loading ----------

        private async Task<bool> LoadAsync(int id, PluginFormInput? formState = null, List<string>? formErrors = null)
        {
            ProfileUser = await _usersService.GetUserByIdAsync(id);

            if (ProfileUser == null)
                return false;

            // 1. Total uploaded contents for this user
            TotalUploads = await _context.Contents
                .Where(c => c.OwnerId == id)
                .CountAsync();

            // 2. Total blocked contents for this user (via ContentActionLogs)
            TotalBlocked = await _context.ContentActionLogs
                .Include(l => l.Content)
                .Where(l => l.Content.OwnerId == id && l.ActionType == ContentActionType.Blocked)
                .CountAsync();

            // 3. Request rate per month (last 12 months)
            var twelveMonthsAgo = DateTime.UtcNow.AddMonths(-11).Date;
            var firstOfMonth = new DateTime(twelveMonthsAgo.Year, twelveMonthsAgo.Month, 1);

            var requestLogs = await _context.ContentActionLogs
                .Include(l => l.Content)
                .Where(l => l.Content.OwnerId == id
                         && l.ActionType == ContentActionType.Requested
                         && l.Timestamp >= firstOfMonth)
                .ToListAsync();

            var requestsPerMonth = requestLogs
                .GroupBy(l => new { l.Timestamp.Year, l.Timestamp.Month })
                .Select(g => new
                {
                    month = $"{g.Key.Year}-{g.Key.Month:D2}",
                    count = g.Count()
                })
                .OrderBy(x => x.month)
                .ToList();

            RequestsPerMonthJson = JsonSerializer.Serialize(requestsPerMonth);

            // 4. Plugins: admins see every plugin, users only those switched on globally
            PluginsPanel = new PluginsPanelModel
            {
                ProfileUserId = id,
                ProfileUsername = ProfileUser.Username,
                IsAdmin = IsAdmin,
                IsSelf = id == CurrentUserId,
                Views = await _plugins.GetViewsForUserAsync(id, includeGloballyDisabled: IsAdmin),
                FormState = formState,
                FormErrors = formErrors ?? new List<string>(),
                Message = PluginMessage,
                Error = PluginError
            };

            // UI widgets always run as the signed-in user, so they are shown on the own profile only.
            if (id == CurrentUserId)
                ProfileWidgets = await PluginUiHost.BuildWidgetsAsync(_plugins, User, "profile");

            return true;
        }
    }
}
