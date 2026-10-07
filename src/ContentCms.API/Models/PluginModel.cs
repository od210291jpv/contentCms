using System.ComponentModel.DataAnnotations;

namespace ContentCms.API.Models
{
    /// <summary>What a plugin provides. A plugin may provide both parts.</summary>
    [Flags]
    public enum PluginKind
    {
        None = 0,
        /// <summary>Server-side script reacting to content events.</summary>
        Backend = 1,
        /// <summary>HTML/JS widget rendered in a sandboxed iframe.</summary>
        Ui = 2,
        /// <summary>Server-side script executed on-demand from the Content Actions menu.</summary>
        ContentAction = 4
    }

    /// <summary>Defines for which users a backend plugin runs when an event is raised.</summary>
    public enum PluginEventScope
    {
        /// <summary>Only for the owner of the content the event is about.</summary>
        Owner = 0,
        /// <summary>For every user the plugin is effectively enabled for (each run uses that user's identity).</summary>
        AllEnabledUsers = 1
    }

    public class PluginModel
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Stable unique identifier (slug), e.g. "slack-notifier".</summary>
        [Required]
        [MaxLength(64)]
        public string Key { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        [MaxLength(32)]
        public string Version { get; set; } = "1.0.0";

        public PluginKind Kind { get; set; } = PluginKind.Backend;

        /// <summary>Global switch. When false the plugin is disabled for everybody.</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>Users without an explicit assignment get this state.</summary>
        public bool EnabledByDefault { get; set; } = false;

        /// <summary>JavaScript source executed by the server-side runtime (Backend kind).</summary>
        public string? Script { get; set; }

        /// <summary>Comma separated event names (e.g. "content.created,content.edited") or "*".</summary>
        [MaxLength(500)]
        public string SubscribedEvents { get; set; } = string.Empty;

        public PluginEventScope EventScope { get; set; } = PluginEventScope.Owner;

        /// <summary>HTML document rendered inside a sandboxed iframe (Ui kind).</summary>
        public string? UiHtml { get; set; }

        /// <summary>Comma separated UI slots: "profile", "dashboard".</summary>
        [MaxLength(100)]
        public string UiSlots { get; set; } = string.Empty;

        /// <summary>JSON describing the per-user configuration form. See docs/PLUGINS.md.</summary>
        public string? ConfigSchemaJson { get; set; }

        public int? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public ICollection<PluginAssignmentModel> Assignments { get; set; } = new List<PluginAssignmentModel>();
    }

    /// <summary>Per-user override of the plugin state plus the user's configuration values.</summary>
    public class PluginAssignmentModel
    {
        [Key]
        public int Id { get; set; }

        public int PluginId { get; set; }
        public PluginModel Plugin { get; set; } = null!;

        public int UserId { get; set; }
        public UserModel User { get; set; } = null!;

        public bool IsEnabled { get; set; }

        /// <summary>JSON object with the user's configuration values.</summary>
        public string? ConfigJson { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Simple key/value storage available to plugin scripts through cms.storage.</summary>
    public class PluginStorageEntry
    {
        [Key]
        public int Id { get; set; }

        public int PluginId { get; set; }
        public PluginModel Plugin { get; set; } = null!;

        public int UserId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Key { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class PluginLogEntry
    {
        [Key]
        public long Id { get; set; }

        public int PluginId { get; set; }
        public PluginModel Plugin { get; set; } = null!;

        public int? UserId { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [MaxLength(10)]
        public string Level { get; set; } = "info";

        /// <summary>What caused the run, e.g. "content.created".</summary>
        [MaxLength(100)]
        public string Trigger { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string Message { get; set; } = string.Empty;
    }
}
