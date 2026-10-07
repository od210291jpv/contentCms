using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using ContentCms.API.DTOs.Events;

namespace ContentCms.API.Services.Plugins
{
    /// <summary>Public names of the events plugins can subscribe to.</summary>
    public static class PluginEventNames
    {
        public const string All = "*";

        public static readonly IReadOnlyList<string> Known = new[]
        {
            "content.created",
            "content.edited",
            "content.deleted",
            "content.reassigned",
            "content.enabled",
            "content.disabled",
            "content.made_public",
            "content.made_private",
            "content.action"
        };

        public static string From(ContentEventType type) => type switch
        {
            ContentEventType.Created => "content.created",
            ContentEventType.Edited => "content.edited",
            ContentEventType.Deleted => "content.deleted",
            ContentEventType.Reassigned => "content.reassigned",
            ContentEventType.Enabled => "content.enabled",
            ContentEventType.Disabled => "content.disabled",
            ContentEventType.MadePublic => "content.made_public",
            ContentEventType.MadePrivate => "content.made_private",
            _ => "content.unknown"
        };

        public static List<string> Parse(string? csv) =>
            (csv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Returns the first invalid event name, or null when all are valid.</summary>
        public static string? FindInvalid(IEnumerable<string> names) =>
            names.FirstOrDefault(n => n != All && !Known.Contains(n, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>An event delivered to the plugin dispatcher.</summary>
    public class PluginEvent
    {
        public required string Name { get; init; }
        public required ContentUpdateEvent Content { get; init; }

        /// <summary>Set when the event was caused by an API call made by a plugin (loop guard).</summary>
        public int? OriginPluginId { get; init; }

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        /// <summary>JSON passed to script handlers as the first argument.</summary>
        public string ToJson()
        {
            var node = JsonSerializer.SerializeToNode(Content, Json)!.AsObject();
            node.Remove("eventType");
            var result = new JsonObject { ["type"] = Name };
            foreach (var kv in node.ToList())
            {
                node.Remove(kv.Key);
                result[kv.Key] = kv.Value;
            }
            return result.ToJsonString();
        }
    }

    /// <summary>
    /// Bounded in-memory queue between the event bus and the plugin dispatcher. Oldest events are dropped
    /// when plugins cannot keep up, so slow plugins never block API requests.
    /// </summary>
    public class PluginEventQueue
    {
        public Channel<PluginEvent> Channel { get; } = System.Threading.Channels.Channel.CreateBounded<PluginEvent>(
            new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
    }
}
