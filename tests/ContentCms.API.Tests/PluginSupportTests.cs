using System.Text.Json.Nodes;
using ContentCms.API.DTOs.Events;
using ContentCms.API.Services.Plugins;
using Xunit;

namespace ContentCms.API.Tests
{
    public class PluginConfigSchemaTests
    {
        private const string Schema = """
        { "fields": [
            { "key": "url", "label": "URL", "type": "string", "required": true },
            { "key": "token", "label": "Token", "type": "secret" },
            { "key": "retries", "label": "Retries", "type": "number", "default": 3 },
            { "key": "verbose", "label": "Verbose", "type": "boolean" },
            { "key": "mode", "label": "Mode", "type": "select", "options": ["a", "b"], "default": "a" }
        ] }
        """;

        private static List<PluginConfigField> Fields()
        {
            Assert.True(PluginConfigSchema.TryParse(Schema, out var fields, out var error), error);
            return fields;
        }

        [Fact]
        public void Empty_schema_is_valid()
        {
            Assert.True(PluginConfigSchema.TryParse(null, out var fields, out _));
            Assert.Empty(fields);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{}")]
        [InlineData("{\"fields\":[{\"key\":\"1bad\"}]}")]
        [InlineData("{\"fields\":[{\"key\":\"a\",\"type\":\"nope\"}]}")]
        [InlineData("{\"fields\":[{\"key\":\"a\"},{\"key\":\"a\"}]}")]
        [InlineData("{\"fields\":[{\"key\":\"a\",\"type\":\"select\"}]}")]
        public void Invalid_schemas_are_rejected(string json)
        {
            Assert.False(PluginConfigSchema.TryParse(json, out _, out var error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void Effective_config_overlays_defaults_with_user_values()
        {
            var cfg = PluginConfigSchema.Effective(Fields(), "{\"url\":\"x\",\"retries\":9}");
            Assert.Equal("x", (string?)cfg["url"]);
            Assert.Equal(9, (int?)cfg["retries"]);
            Assert.Equal("a", (string?)cfg["mode"]);
            Assert.False((bool?)cfg["verbose"]);
        }

        [Fact]
        public void Normalize_validates_and_coerces()
        {
            var ok = PluginConfigSchema.TryNormalize(Fields(),
                new JsonObject { ["url"] = "https://x", ["retries"] = "5", ["verbose"] = "on", ["mode"] = "b", ["token"] = "s3cret" },
                new JsonObject(), out var result, out var errors);

            Assert.True(ok, string.Join(";", errors));
            Assert.Equal(5d, (double?)result["retries"]);
            Assert.True((bool?)result["verbose"]);
            Assert.Equal("s3cret", (string?)result["token"]);
        }

        [Fact]
        public void Normalize_reports_required_and_invalid_values()
        {
            var ok = PluginConfigSchema.TryNormalize(Fields(),
                new JsonObject { ["url"] = "", ["retries"] = "abc", ["mode"] = "zzz" },
                new JsonObject(), out _, out var errors);

            Assert.False(ok);
            Assert.Equal(3, errors.Count);
        }

        [Fact]
        public void Masked_or_empty_secret_keeps_existing_value()
        {
            var existing = new JsonObject { ["token"] = "old" };
            PluginConfigSchema.TryNormalize(Fields(),
                new JsonObject { ["url"] = "x", ["token"] = PluginConfigSchema.SecretMask },
                existing, out var result, out _);

            Assert.Equal("old", (string?)result["token"]);
            Assert.Equal(PluginConfigSchema.SecretMask,
                (string?)PluginConfigSchema.MaskSecrets(Fields(), result)["token"]);
        }
    }

    public class PluginTokenServiceTests
    {
        [Fact]
        public void Issued_tokens_validate_until_expiry_or_revoke()
        {
            var svc = new PluginTokenService();
            var token = svc.Issue(5, 9, TimeSpan.FromMinutes(1));

            var info = svc.Validate(token);
            Assert.NotNull(info);
            Assert.Equal(5, info!.UserId);
            Assert.Equal(9, info.PluginId);

            svc.Revoke(token);
            Assert.Null(svc.Validate(token));
        }

        [Fact]
        public void Expired_and_foreign_tokens_are_rejected()
        {
            var svc = new PluginTokenService();
            var expired = svc.Issue(1, 1, TimeSpan.FromSeconds(-1));
            Assert.Null(svc.Validate(expired));
            Assert.Null(svc.Validate("MTpBZG1pbg=="));
            Assert.Null(svc.Validate(null));
            Assert.Null(svc.Validate("plg_unknown"));
        }
    }

    public class PluginEventTests
    {
        [Fact]
        public void Event_json_has_type_first_and_camel_case_fields()
        {
            var ev = new PluginEvent
            {
                Name = "content.created",
                Content = new ContentUpdateEvent { EventType = ContentEventType.Created, Id = 3, OwnerId = 8, Path = "p" }
            };
            var node = JsonNode.Parse(ev.ToJson())!.AsObject();

            Assert.Equal("type", node.First().Key);
            Assert.Equal("content.created", (string?)node["type"]);
            Assert.Equal(3, (int?)node["id"]);
            Assert.Equal(8, (int?)node["ownerId"]);
            Assert.Null(node["eventType"]);
        }

        [Fact]
        public void Every_content_event_type_has_a_known_name()
        {
            foreach (var t in Enum.GetValues<ContentEventType>())
                Assert.Contains(PluginEventNames.From(t), PluginEventNames.Known);
        }

        [Fact]
        public void Event_name_parsing()
        {
            Assert.Null(PluginEventNames.FindInvalid(PluginEventNames.Parse("content.created, *")));
            Assert.Equal("content.nope", PluginEventNames.FindInvalid(PluginEventNames.Parse("content.nope")));
        }
    }
}
