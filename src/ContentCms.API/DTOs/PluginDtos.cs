using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ContentCms.API.Models;
using ContentCms.API.Services.Plugins;

namespace ContentCms.API.DTOs
{
    /// <summary>Plugin definition as accepted by create/update (admin only).</summary>
    public class PluginInputDto
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Version { get; set; } = "1.0.0";

        /// <summary>"Backend", "Ui" or "Backend, Ui".</summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PluginKind Kind { get; set; } = PluginKind.Backend;

        public bool IsEnabled { get; set; } = true;
        public bool EnabledByDefault { get; set; }
        public string? Script { get; set; }

        /// <summary>Event names, e.g. ["content.created"] or ["*"].</summary>
        public List<string> SubscribedEvents { get; set; } = new();

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PluginEventScope EventScope { get; set; } = PluginEventScope.Owner;

        public string? UiHtml { get; set; }

        /// <summary>UI slots: "profile", "dashboard".</summary>
        public List<string> UiSlots { get; set; } = new();

        /// <summary>See docs/PLUGINS.md for the schema format.</summary>
        public string? ConfigSchemaJson { get; set; }

        public PluginInput ToInput() => new()
        {
            Key = Key,
            Name = Name,
            Description = Description,
            Version = Version,
            Kind = Kind,
            IsEnabled = IsEnabled,
            EnabledByDefault = EnabledByDefault,
            Script = Script,
            SubscribedEvents = string.Join(",", SubscribedEvents ?? new List<string>()),
            EventScope = EventScope,
            UiHtml = UiHtml,
            UiSlots = string.Join(",", UiSlots ?? new List<string>()),
            ConfigSchemaJson = ConfigSchemaJson
        };
    }

    /// <summary>Plugin as returned by the API, from the point of view of one user.</summary>
    public class PluginDto
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Version { get; set; } = string.Empty;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PluginKind Kind { get; set; }

        /// <summary>Global switch.</summary>
        public bool IsEnabled { get; set; }
        public bool EnabledByDefault { get; set; }

        /// <summary>Whether the plugin is active for the user this response is about.</summary>
        public bool EffectiveEnabled { get; set; }

        /// <summary>Explicit per-user switch, or null when the plugin default applies.</summary>
        public bool? UserOverride { get; set; }

        public List<string> SubscribedEvents { get; set; } = new();

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PluginEventScope EventScope { get; set; }

        public List<string> UiSlots { get; set; } = new();
        public string? ConfigSchemaJson { get; set; }

        /// <summary>The user's configuration (secrets masked).</summary>
        public JsonObject Config { get; set; } = new();

        // Admin-only fields
        public string? Script { get; set; }
        public string? UiHtml { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static PluginDto From(PluginView view, bool includeSource)
        {
            var p = view.Plugin;
            return new PluginDto
            {
                Id = p.Id,
                Key = p.Key,
                Name = p.Name,
                Description = p.Description,
                Version = p.Version,
                Kind = p.Kind,
                IsEnabled = p.IsEnabled,
                EnabledByDefault = p.EnabledByDefault,
                EffectiveEnabled = view.EffectiveEnabled,
                UserOverride = view.UserOverride,
                SubscribedEvents = PluginEventNames.Parse(p.SubscribedEvents),
                EventScope = p.EventScope,
                UiSlots = PluginEventNames.Parse(p.UiSlots),
                ConfigSchemaJson = p.ConfigSchemaJson,
                Config = view.Config,
                Script = includeSource ? p.Script : null,
                UiHtml = includeSource ? p.UiHtml : null,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            };
        }
    }

    public class PluginLogDto
    {
        public long Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Trigger { get; set; } = string.Empty;
        public int? UserId { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
