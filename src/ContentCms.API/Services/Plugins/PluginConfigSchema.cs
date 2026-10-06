using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ContentCms.API.Services.Plugins
{
    public class PluginConfigField
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Type { get; set; } = "string";
        public string? Description { get; set; }
        public bool Required { get; set; }
        public JsonNode? Default { get; set; }
        public List<string> Options { get; set; } = new();
    }

    /// <summary>
    /// Parses and applies the per-user configuration schema of a plugin:
    /// <c>{ "fields": [ { "key": "url", "label": "Webhook URL", "type": "string", "required": true } ] }</c>.
    /// Supported types: string, text, number, boolean, secret, select.
    /// </summary>
    public static class PluginConfigSchema
    {
        public const string SecretMask = "********";
        public static readonly string[] Types = { "string", "text", "number", "boolean", "secret", "select" };

        private static readonly Regex KeyRegex = new("^[A-Za-z_][A-Za-z0-9_]{0,49}$", RegexOptions.Compiled);

        public static bool TryParse(string? json, out List<PluginConfigField> fields, out string? error)
        {
            fields = new List<PluginConfigField>();
            error = null;

            if (string.IsNullOrWhiteSpace(json))
                return true;

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch (JsonException ex)
            {
                error = $"Config schema is not valid JSON: {ex.Message}";
                return false;
            }

            if (root is not JsonObject obj || obj["fields"] is not JsonArray array)
            {
                error = "Config schema must be an object with a \"fields\" array.";
                return false;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in array)
            {
                if (item is not JsonObject f)
                {
                    error = "Every config field must be an object.";
                    return false;
                }

                var key = f["key"]?.GetValue<string>() ?? string.Empty;
                if (!KeyRegex.IsMatch(key))
                {
                    error = $"Invalid config field key '{key}'. Use letters, digits and underscores (max 50), not starting with a digit.";
                    return false;
                }
                if (!seen.Add(key))
                {
                    error = $"Duplicate config field key '{key}'.";
                    return false;
                }

                var type = (f["type"]?.GetValue<string>() ?? "string").ToLowerInvariant();
                if (!Types.Contains(type))
                {
                    error = $"Unsupported config field type '{type}' for '{key}'. Supported: {string.Join(", ", Types)}.";
                    return false;
                }

                var field = new PluginConfigField
                {
                    Key = key,
                    Label = f["label"]?.GetValue<string>() ?? key,
                    Type = type,
                    Description = f["description"]?.GetValue<string>(),
                    Required = f["required"]?.GetValue<bool>() ?? false,
                    Default = f["default"]?.DeepClone()
                };

                if (type == "select")
                {
                    if (f["options"] is not JsonArray opts || opts.Count == 0)
                    {
                        error = $"Select field '{key}' needs a non-empty \"options\" array.";
                        return false;
                    }
                    field.Options = opts.Select(o => o?.ToString() ?? string.Empty).ToList();
                }

                fields.Add(field);
            }

            return true;
        }

        /// <summary>Default values of all fields.</summary>
        public static JsonObject Defaults(IEnumerable<PluginConfigField> fields)
        {
            var result = new JsonObject();
            foreach (var f in fields)
            {
                if (f.Default != null)
                    result[f.Key] = f.Default.DeepClone();
                else if (f.Type == "boolean")
                    result[f.Key] = false;
            }
            return result;
        }

        /// <summary>Effective configuration passed to the script: defaults overlaid with the user's stored values.</summary>
        public static JsonObject Effective(IEnumerable<PluginConfigField> fields, string? userConfigJson)
        {
            var result = Defaults(fields);
            var stored = ParseObject(userConfigJson);
            foreach (var f in fields)
            {
                if (stored.TryGetPropertyValue(f.Key, out var v) && v != null)
                    result[f.Key] = v.DeepClone();
            }
            return result;
        }

        /// <summary>Replaces secret values with a mask so they are never sent back to the browser.</summary>
        public static JsonObject MaskSecrets(IEnumerable<PluginConfigField> fields, JsonObject config)
        {
            var result = (JsonObject)config.DeepClone();
            foreach (var f in fields.Where(f => f.Type == "secret"))
            {
                if (result[f.Key] is JsonNode n && !string.IsNullOrEmpty(n.ToString()))
                    result[f.Key] = SecretMask;
            }
            return result;
        }

        /// <summary>
        /// Validates user input against the schema. Keys missing from the input keep their existing value
        /// (or default). Secret values that are empty or masked keep the existing secret.
        /// </summary>
        public static bool TryNormalize(
            IReadOnlyList<PluginConfigField> fields,
            JsonObject input,
            JsonObject existing,
            out JsonObject result,
            out List<string> errors)
        {
            result = new JsonObject();
            errors = new List<string>();
            var defaults = Defaults(fields);

            foreach (var f in fields)
            {
                JsonNode? value = null;
                var provided = input.TryGetPropertyValue(f.Key, out var raw);

                if (!provided)
                {
                    value = existing[f.Key]?.DeepClone() ?? defaults[f.Key]?.DeepClone();
                }
                else
                {
                    switch (f.Type)
                    {
                        case "string":
                        case "text":
                        {
                            var s = raw?.ToString() ?? string.Empty;
                            var max = f.Type == "text" ? 10000 : 2000;
                            if (s.Length > max) errors.Add($"'{f.Label}' is too long (max {max}).");
                            value = s;
                            break;
                        }
                        case "secret":
                        {
                            var s = raw?.ToString() ?? string.Empty;
                            if (s.Length == 0 || s == SecretMask)
                                value = existing[f.Key]?.DeepClone();
                            else if (s.Length > 2000)
                                errors.Add($"'{f.Label}' is too long (max 2000).");
                            else
                                value = s;
                            break;
                        }
                        case "number":
                        {
                            var s = raw?.ToString();
                            if (string.IsNullOrWhiteSpace(s))
                                value = null;
                            else if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                                         System.Globalization.CultureInfo.InvariantCulture, out var d)
                                     && !double.IsNaN(d) && !double.IsInfinity(d))
                                value = d;
                            else
                                errors.Add($"'{f.Label}' must be a number.");
                            break;
                        }
                        case "boolean":
                        {
                            var s = raw?.ToString()?.Trim().ToLowerInvariant();
                            value = s is "true" or "on" or "1" or "yes";
                            break;
                        }
                        case "select":
                        {
                            var s = raw?.ToString() ?? string.Empty;
                            if (s.Length == 0)
                                value = null;
                            else if (!f.Options.Contains(s))
                                errors.Add($"'{f.Label}' must be one of: {string.Join(", ", f.Options)}.");
                            else
                                value = s;
                            break;
                        }
                    }
                }

                if (f.Required && f.Type != "boolean" && (value == null || string.IsNullOrEmpty(value.ToString())))
                    errors.Add($"'{f.Label}' is required.");

                if (value != null)
                    result[f.Key] = value;
            }

            return errors.Count == 0;
        }

        public static JsonObject ParseObject(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
            try
            {
                return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                return new JsonObject();
            }
        }
    }
}
