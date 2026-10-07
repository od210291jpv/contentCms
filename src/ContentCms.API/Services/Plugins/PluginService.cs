using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContentCms.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContentCms.API.Services.Plugins
{
    /// <summary>Editable plugin definition (admin input).</summary>
    public class PluginInput
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Version { get; set; } = "1.0.0";
        public PluginKind Kind { get; set; } = PluginKind.Backend;
        public bool IsEnabled { get; set; } = true;
        public bool EnabledByDefault { get; set; }
        public string? Script { get; set; }
        /// <summary>Comma separated event names or "*".</summary>
        public string? SubscribedEvents { get; set; }
        public PluginEventScope EventScope { get; set; } = PluginEventScope.Owner;
        public string? UiHtml { get; set; }
        /// <summary>Comma separated slots: profile, dashboard.</summary>
        public string? UiSlots { get; set; }
        public string? ConfigSchemaJson { get; set; }
    }

    /// <summary>A plugin as seen by one particular user.</summary>
    public class PluginView
    {
        public required PluginModel Plugin { get; init; }
        public required bool EffectiveEnabled { get; init; }
        /// <summary>Explicit per-user switch; null when the user has no assignment (plugin default applies).</summary>
        public bool? UserOverride { get; init; }
        public required List<PluginConfigField> Fields { get; init; }
        /// <summary>Stored user config with secrets masked.</summary>
        public required JsonObject Config { get; init; }
    }

    public record PluginRunTarget(PluginModel Plugin, UserModel User, JsonObject Config);

    public interface IPluginService
    {
        Task<List<PluginModel>> GetAllAsync();
        Task<PluginModel?> GetAsync(int id);
        Task<(PluginModel? Plugin, List<string> Errors)> CreateAsync(PluginInput input, int? createdByUserId);
        Task<(PluginModel? Plugin, List<string> Errors)> UpdateAsync(int id, PluginInput input);
        Task<bool> DeleteAsync(int id);

        Task<List<PluginView>> GetViewsForUserAsync(int userId, bool includeGloballyDisabled);
        Task<PluginView?> GetViewAsync(int pluginId, int userId);
        Task<bool> SetUserEnabledAsync(int pluginId, int userId, bool enabled);
        Task<(bool Ok, List<string> Errors)> SaveUserConfigAsync(int pluginId, int userId, JsonObject input);
        Task<List<PluginView>> GetUiWidgetsAsync(int userId, string slot);

        Task<List<PluginRunTarget>> GetRunTargetsAsync(string eventName, int ownerId);
        Task<List<PluginLogEntry>> GetLogsAsync(int pluginId, int take = 50);
        Task WriteLogsAsync(int pluginId, int? userId, string trigger, IReadOnlyCollection<(string Level, string Message)> entries);
        Task<bool> ExecuteContentActionAsync(int pluginId, int userId, ContentModel content);
    }

    public class PluginService : IPluginService
    {
        public const int MaxSourceLength = 200_000;
        public static readonly string[] UiSlotNames = { "profile", "dashboard" };

        private static readonly Regex KeyRegex = new("^[a-z0-9][a-z0-9._-]{1,63}$", RegexOptions.Compiled);
        private const int MaxLogsPerPlugin = 1000;

        private readonly ContentCmsDbContext _context;
        private readonly IServiceProvider _services;

        public PluginService(ContentCmsDbContext context, IServiceProvider services)
        {
            _context = context;
            _services = services;
        }

        public static bool IsEffectivelyEnabled(PluginModel plugin, PluginAssignmentModel? assignment) =>
            plugin.IsEnabled && (assignment?.IsEnabled ?? plugin.EnabledByDefault);

        // ---------- definitions ----------

        public Task<List<PluginModel>> GetAllAsync() =>
            _context.Plugins.AsNoTracking().OrderBy(p => p.Name).ToListAsync();

        public Task<PluginModel?> GetAsync(int id) =>
            _context.Plugins.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);

        public async Task<(PluginModel? Plugin, List<string> Errors)> CreateAsync(PluginInput input, int? createdByUserId)
        {
            var errors = Validate(input);
            var key = (input.Key ?? string.Empty).Trim().ToLowerInvariant();
            if (errors.Count == 0 && await _context.Plugins.AnyAsync(p => p.Key == key))
                errors.Add($"A plugin with key '{key}' already exists.");
            if (errors.Count > 0)
                return (null, errors);

            var plugin = new PluginModel { Key = key, CreatedByUserId = createdByUserId };
            Apply(plugin, input);
            _context.Plugins.Add(plugin);
            await _context.SaveChangesAsync();
            return (plugin, errors);
        }

        public async Task<(PluginModel? Plugin, List<string> Errors)> UpdateAsync(int id, PluginInput input)
        {
            var plugin = await _context.Plugins.FirstOrDefaultAsync(p => p.Id == id);
            if (plugin == null)
                return (null, new List<string>());

            var errors = Validate(input);
            if (!string.IsNullOrWhiteSpace(input.Key) &&
                !string.Equals(input.Key.Trim(), plugin.Key, StringComparison.OrdinalIgnoreCase))
                errors.Add("The plugin key cannot be changed.");
            if (errors.Count > 0)
                return (null, errors);

            Apply(plugin, input);
            plugin.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return (plugin, errors);
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var plugin = await _context.Plugins.FirstOrDefaultAsync(p => p.Id == id);
            if (plugin == null) return false;
            _context.Plugins.Remove(plugin);
            await _context.SaveChangesAsync();
            return true;
        }

        private static List<string> Validate(PluginInput input)
        {
            var errors = new List<string>();

            if (!KeyRegex.IsMatch((input.Key ?? string.Empty).Trim().ToLowerInvariant()))
                errors.Add("Key must be 2-64 characters: lowercase letters, digits, '.', '_' or '-', starting with a letter or digit.");
            if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 150)
                errors.Add("Name is required (max 150 characters).");
            if (input.Description?.Length > 1000)
                errors.Add("Description is too long (max 1000 characters).");
            if (string.IsNullOrWhiteSpace(input.Version) || input.Version.Length > 32)
                errors.Add("Version is required (max 32 characters).");
            if (input.Kind == PluginKind.None || (input.Kind & ~(PluginKind.Backend | PluginKind.Ui | PluginKind.ContentAction)) != 0)
                errors.Add("Select at least one plugin kind (Backend, UI, or Content Action).");

            if (input.Kind.HasFlag(PluginKind.Backend) || input.Kind.HasFlag(PluginKind.ContentAction))
            {
                if (string.IsNullOrWhiteSpace(input.Script))
                    errors.Add("A backend plugin requires a script.");
                else if (input.Script.Length > MaxSourceLength)
                    errors.Add($"Script is too long (max {MaxSourceLength} characters).");
                else
                {
                    try { PluginScriptRunner.ValidateSyntax(input.Script); }
                    catch (Exception ex) { errors.Add($"Script syntax error: {ex.Message}"); }
                }

                var events = PluginEventNames.Parse(input.SubscribedEvents);
                if (events.Count == 0)
                    errors.Add("A backend plugin must subscribe to at least one event.");
                else if (PluginEventNames.FindInvalid(events) is { } bad)
                    errors.Add($"Unknown event '{bad}'. Known events: {string.Join(", ", PluginEventNames.Known)}, *.");
            }

            if (input.Kind.HasFlag(PluginKind.Ui))
            {
                if (string.IsNullOrWhiteSpace(input.UiHtml))
                    errors.Add("A UI plugin requires HTML.");
                else if (input.UiHtml.Length > MaxSourceLength)
                    errors.Add($"UI HTML is too long (max {MaxSourceLength} characters).");

                var slots = PluginEventNames.Parse(input.UiSlots);
                if (slots.Count == 0)
                    errors.Add("A UI plugin must choose at least one slot.");
                else if (slots.FirstOrDefault(s => !UiSlotNames.Contains(s, StringComparer.OrdinalIgnoreCase)) is { } badSlot)
                    errors.Add($"Unknown UI slot '{badSlot}'. Known slots: {string.Join(", ", UiSlotNames)}.");
            }

            if (!PluginConfigSchema.TryParse(input.ConfigSchemaJson, out _, out var schemaError))
                errors.Add(schemaError!);

            return errors;
        }

        private static void Apply(PluginModel plugin, PluginInput input)
        {
            plugin.Name = input.Name.Trim();
            plugin.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
            plugin.Version = input.Version.Trim();
            plugin.Kind = input.Kind;
            plugin.IsEnabled = input.IsEnabled;
            plugin.EnabledByDefault = input.EnabledByDefault;
            plugin.EventScope = input.EventScope;
            plugin.ConfigSchemaJson = string.IsNullOrWhiteSpace(input.ConfigSchemaJson) ? null : input.ConfigSchemaJson;

            var backend = input.Kind.HasFlag(PluginKind.Backend) || input.Kind.HasFlag(PluginKind.ContentAction);
            plugin.Script = backend ? input.Script : null;
            plugin.SubscribedEvents = backend
                ? string.Join(",", PluginEventNames.Parse(input.SubscribedEvents).Select(e => e == PluginEventNames.All ? e : e.ToLowerInvariant()))
                : string.Empty;

            var ui = input.Kind.HasFlag(PluginKind.Ui);
            plugin.UiHtml = ui ? input.UiHtml : null;
            plugin.UiSlots = ui
                ? string.Join(",", PluginEventNames.Parse(input.UiSlots).Select(s => s.ToLowerInvariant()))
                : string.Empty;
        }

        // ---------- per-user state ----------

        public async Task<List<PluginView>> GetViewsForUserAsync(int userId, bool includeGloballyDisabled)
        {
            var plugins = await _context.Plugins.AsNoTracking()
                .Where(p => includeGloballyDisabled || p.IsEnabled)
                .OrderBy(p => p.Name)
                .ToListAsync();
            var assignments = await _context.PluginAssignments.AsNoTracking()
                .Where(a => a.UserId == userId)
                .ToDictionaryAsync(a => a.PluginId);

            return plugins.Select(p =>
            {
                assignments.TryGetValue(p.Id, out var a);
                return BuildView(p, a);
            }).ToList();
        }

        public async Task<PluginView?> GetViewAsync(int pluginId, int userId)
        {
            var plugin = await _context.Plugins.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pluginId);
            if (plugin == null) return null;
            var assignment = await _context.PluginAssignments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.PluginId == pluginId && a.UserId == userId);
            return BuildView(plugin, assignment);
        }

        private static PluginView BuildView(PluginModel plugin, PluginAssignmentModel? assignment)
        {
            PluginConfigSchema.TryParse(plugin.ConfigSchemaJson, out var fields, out _);
            var config = PluginConfigSchema.MaskSecrets(fields, PluginConfigSchema.Effective(fields, assignment?.ConfigJson));
            return new PluginView
            {
                Plugin = plugin,
                EffectiveEnabled = IsEffectivelyEnabled(plugin, assignment),
                UserOverride = assignment?.IsEnabled,
                Fields = fields,
                Config = config
            };
        }

        public async Task<bool> SetUserEnabledAsync(int pluginId, int userId, bool enabled)
        {
            if (!await _context.Plugins.AnyAsync(p => p.Id == pluginId) ||
                !await _context.Users.AnyAsync(u => u.Id == userId))
                return false;

            var assignment = await _context.PluginAssignments
                .FirstOrDefaultAsync(a => a.PluginId == pluginId && a.UserId == userId);
            if (assignment == null)
            {
                assignment = new PluginAssignmentModel { PluginId = pluginId, UserId = userId };
                _context.PluginAssignments.Add(assignment);
            }
            assignment.IsEnabled = enabled;
            assignment.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<(bool Ok, List<string> Errors)> SaveUserConfigAsync(int pluginId, int userId, JsonObject input)
        {
            var plugin = await _context.Plugins.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pluginId);
            if (plugin == null || !await _context.Users.AnyAsync(u => u.Id == userId))
                return (false, new List<string> { "Plugin or user not found." });

            PluginConfigSchema.TryParse(plugin.ConfigSchemaJson, out var fields, out _);

            var assignment = await _context.PluginAssignments
                .FirstOrDefaultAsync(a => a.PluginId == pluginId && a.UserId == userId);
            var existing = PluginConfigSchema.ParseObject(assignment?.ConfigJson);

            if (!PluginConfigSchema.TryNormalize(fields, input, existing, out var normalized, out var errors))
                return (false, errors);

            if (assignment == null)
            {
                // No explicit switch yet: keep the plugin default so saving a config does not enable/disable anything.
                assignment = new PluginAssignmentModel
                {
                    PluginId = pluginId,
                    UserId = userId,
                    IsEnabled = plugin.EnabledByDefault
                };
                _context.PluginAssignments.Add(assignment);
            }
            assignment.ConfigJson = normalized.ToJsonString();
            assignment.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return (true, errors);
        }

        public async Task<List<PluginView>> GetUiWidgetsAsync(int userId, string slot)
        {
            var views = await GetViewsForUserAsync(userId, includeGloballyDisabled: false);
            return views
                .Where(v => v.EffectiveEnabled
                            && v.Plugin.Kind.HasFlag(PluginKind.Ui)
                            && PluginEventNames.Parse(v.Plugin.UiSlots).Contains(slot, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        // ---------- runtime support ----------

        public async Task<List<PluginRunTarget>> GetRunTargetsAsync(string eventName, int ownerId)
        {
            var plugins = await _context.Plugins.AsNoTracking()
                .Where(p => p.IsEnabled && (p.Kind & PluginKind.Backend) != 0 && p.Script != null)
                .ToListAsync();

            plugins = plugins.Where(p =>
            {
                var subscribed = PluginEventNames.Parse(p.SubscribedEvents);
                return subscribed.Contains(PluginEventNames.All) ||
                       subscribed.Contains(eventName, StringComparer.OrdinalIgnoreCase);
            }).ToList();

            var targets = new List<PluginRunTarget>();
            foreach (var plugin in plugins)
            {
                IQueryable<UserModel> users = _context.Users.AsNoTracking().Where(u => u.IsActive && !u.IsDeleted);
                if (plugin.EventScope == PluginEventScope.Owner)
                    users = users.Where(u => u.Id == ownerId);

                var userList = await users.ToListAsync();
                if (userList.Count == 0) continue;

                var ids = userList.Select(u => u.Id).ToList();
                var assignments = await _context.PluginAssignments.AsNoTracking()
                    .Where(a => a.PluginId == plugin.Id && ids.Contains(a.UserId))
                    .ToDictionaryAsync(a => a.UserId);

                PluginConfigSchema.TryParse(plugin.ConfigSchemaJson, out var fields, out _);

                foreach (var user in userList)
                {
                    assignments.TryGetValue(user.Id, out var assignment);
                    if (!IsEffectivelyEnabled(plugin, assignment)) continue;
                    targets.Add(new PluginRunTarget(plugin, user, PluginConfigSchema.Effective(fields, assignment?.ConfigJson)));
                }
            }
            return targets;
        }

        public Task<List<PluginLogEntry>> GetLogsAsync(int pluginId, int take = 50) =>
            _context.PluginLogEntries.AsNoTracking()
                .Where(l => l.PluginId == pluginId)
                .OrderByDescending(l => l.Id)
                .Take(Math.Clamp(take, 1, 200))
                .ToListAsync();

        public async Task WriteLogsAsync(int pluginId, int? userId, string trigger, IReadOnlyCollection<(string Level, string Message)> entries)
        {
            if (entries.Count == 0) return;

            foreach (var (level, message) in entries)
            {
                _context.PluginLogEntries.Add(new PluginLogEntry
                {
                    PluginId = pluginId,
                    UserId = userId,
                    Level = level.Length > 10 ? level[..10] : level,
                    Trigger = trigger.Length > 100 ? trigger[..100] : trigger,
                    Message = message.Length > 2000 ? message[..2000] : message
                });
            }
            await _context.SaveChangesAsync();

            // Keep the log bounded: occasionally drop the oldest entries.
            var count = await _context.PluginLogEntries.CountAsync(l => l.PluginId == pluginId);
            if (count > MaxLogsPerPlugin)
            {
                var cutoff = await _context.PluginLogEntries
                    .Where(l => l.PluginId == pluginId)
                    .OrderByDescending(l => l.Id)
                    .Skip(MaxLogsPerPlugin - 200)
                    .Select(l => l.Id)
                    .FirstAsync();
                await _context.PluginLogEntries
                    .Where(l => l.PluginId == pluginId && l.Id <= cutoff)
                    .ExecuteDeleteAsync();
            }
        }

        public async Task<bool> ExecuteContentActionAsync(int pluginId, int userId, ContentModel content)
        {
            var plugin = await _context.Plugins.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pluginId);
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            
            if (plugin == null || user == null) return false;
            
            var assignment = await _context.PluginAssignments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.PluginId == pluginId && a.UserId == userId);
                
            if (!IsEffectivelyEnabled(plugin, assignment) || !plugin.Kind.HasFlag(PluginKind.ContentAction))
                return false;

            PluginConfigSchema.TryParse(plugin.ConfigSchemaJson, out var fields, out _);
            var config = PluginConfigSchema.Effective(fields, assignment?.ConfigJson);

            var updateEvent = new ContentCms.API.DTOs.Events.ContentUpdateEvent
            {
                Id = content.Id,
                OwnerId = content.OwnerId,
                Enabled = content.Enabled,
                Description = content.Description,
                Path = content.Path,
                IsPublic = content.IsPublic,
                IsDeleted = content.IsDeleted,
                CreatedAt = content.CreatedAt,
                UpdatedAt = content.UpdatedAt,
                DeletedAt = content.DeletedAt
            };

            var pluginEvent = new PluginEvent 
            { 
                Name = "content.action",
                Content = updateEvent 
            };
            
            var target = new PluginRunTarget(plugin, user, config);
            
            var runtime = _services.GetRequiredService<IPluginRuntime>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await runtime.RunEventAsync(target, pluginEvent, cts.Token);
            
            return result.Success;
        }
    }
}
