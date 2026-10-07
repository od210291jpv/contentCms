using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContentCms.API.Models;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;

namespace ContentCms.API.Services.Plugins
{
    public interface IPluginRuntime
    {
        /// <summary>Runs the plugin script for one user and one event, persisting its log output.</summary>
        Task<PluginRunResult> RunEventAsync(PluginRunTarget target, PluginEvent pluginEvent, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Runs backend plugins. Gives scripts access to the application's own HTTP API (as the target user, through a
    /// short-lived scoped token), outbound HTTP, per-user storage and logging.
    /// </summary>
    public class PluginRuntime : IPluginRuntime
    {
        public const string LoopbackClientName = "plugin-loopback";
        public const string ExternalClientName = "plugin-external";

        private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(30);

        private readonly ContentCmsDbContext _context;
        private readonly IPluginService _plugins;
        private readonly IPluginTokenService _tokens;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServer _server;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PluginRuntime> _logger;

        public PluginRuntime(
            ContentCmsDbContext context,
            IPluginService plugins,
            IPluginTokenService tokens,
            IHttpClientFactory httpClientFactory,
            IServer server,
            IConfiguration configuration,
            ILogger<PluginRuntime> logger)
        {
            _context = context;
            _plugins = plugins;
            _tokens = tokens;
            _httpClientFactory = httpClientFactory;
            _server = server;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<PluginRunResult> RunEventAsync(PluginRunTarget target, PluginEvent pluginEvent, CancellationToken cancellationToken)
        {
            var plugin = target.Plugin;
            var token = _tokens.Issue(target.User.Id, plugin.Id, TokenLifetime);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(OverallTimeout);

            var host = new RunHost(this, plugin.Id, target.User.Id, token, cts.Token);
            PluginRunResult result;
            try
            {
                var context = new PluginRunContext
                {
                    PluginId = plugin.Id,
                    PluginKey = plugin.Key,
                    PluginName = plugin.Name,
                    PluginVersion = plugin.Version,
                    UserId = target.User.Id,
                    Username = target.User.Username,
                    Role = target.User.Role.ToString(),
                    Trigger = pluginEvent.Name,
                    Config = target.Config
                };

                // Jint is synchronous: run it on a worker thread so the host's blocking calls never stall the dispatcher loop.
                result = await Task.Run(() => PluginScriptRunner.Run(
                    plugin.Script ?? string.Empty, context, pluginEvent.Name, pluginEvent.ToJson(), host, cancellationToken: cts.Token),
                    cts.Token);
            }
            catch (Exception ex)
            {
                result = new PluginRunResult(false, 0, $"Plugin run failed: {ex.Message}");
            }
            finally
            {
                _tokens.Revoke(token);
            }

            try
            {
                var entries = host.Logs.Take(100).ToList();
                if (!result.Success)
                    entries.Add(("error", result.Error ?? "Unknown error"));
                await _plugins.WriteLogsAsync(plugin.Id, target.User.Id, pluginEvent.Name, entries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist logs of plugin {Plugin}", plugin.Key);
            }

            if (!result.Success)
                _logger.LogWarning("Plugin {Plugin} failed for user {UserId} on {Event}: {Error}",
                    plugin.Key, target.User.Id, pluginEvent.Name, result.Error);

            return result;
        }

        private Uri ResolveBaseUri()
        {
            var configured = _configuration["Plugins:ApiBaseUrl"];
            if (!string.IsNullOrWhiteSpace(configured) && Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri))
                return configuredUri;

            var address = _server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
                          ?? "http://localhost:8080";

            // Wildcard bindings ("http://+:8080", "http://*:80", "http://[::]:80") are not valid request targets.
            address = address.Replace("://+", "://localhost").Replace("://*", "://localhost")
                .Replace("://0.0.0.0", "://localhost").Replace("://[::]", "://localhost");
            return Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : new Uri("http://localhost:8080");
        }

        private sealed class RunHost : IPluginHost
        {
            private const int MaxApiCalls = 50;
            private const int MaxHttpCalls = 20;
            private const int MaxStorageOps = 200;
            private const int MaxStorageEntries = 200;
            private const int MaxStorageValueBytes = 64 * 1024;
            private const int MaxResponseBytes = 1024 * 1024;

            private readonly PluginRuntime _rt;
            private readonly int _pluginId;
            private readonly int _userId;
            private readonly string _token;
            private readonly CancellationToken _ct;
            private int _apiCalls, _httpCalls, _storageOps;

            public List<(string Level, string Message)> Logs { get; } = new();

            public RunHost(PluginRuntime rt, int pluginId, int userId, string token, CancellationToken ct)
            {
                _rt = rt;
                _pluginId = pluginId;
                _userId = userId;
                _token = token;
                _ct = ct;
            }

            public void Log(string level, string message)
            {
                if (Logs.Count < 200)
                    Logs.Add((level is "debug" or "info" or "warn" or "error" ? level : "info", message));
            }

            // ---- cms.api ----
            public string Api(string method, string path, string? bodyJson)
            {
                if (++_apiCalls > MaxApiCalls)
                    return Error($"cms.api call limit ({MaxApiCalls}) per run exceeded.");
                if (method is not ("GET" or "POST" or "PUT" or "DELETE" or "PATCH"))
                    return Error($"Unsupported HTTP method '{method}'.");

                var baseUri = _rt.ResolveBaseUri();
                if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path.Contains("//") || path.Contains('\\'))
                    return Error("cms.api paths must be relative and start with /api/.");
                if (!Uri.TryCreate(baseUri, path, out var uri) || uri.Host != baseUri.Host || uri.Port != baseUri.Port
                    || !uri.AbsolutePath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                    return Error("Invalid cms.api path.");

                using var request = new HttpRequestMessage(new HttpMethod(method), uri);
                // The CMS API expects the raw token in the Authorization header.
                request.Headers.TryAddWithoutValidation("Authorization", _token);

                var envelope = ParseObject(bodyJson);
                var body = envelope["body"];
                if (body != null && method is "POST" or "PUT" or "PATCH")
                {
                    if (envelope["form"]?.GetValue<bool>() == true && body is JsonObject form)
                    {
                        var pairs = form.Select(kv => new KeyValuePair<string, string>(kv.Key, FormValue(kv.Value)));
                        request.Content = new FormUrlEncodedContent(pairs);
                    }
                    else
                    {
                        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                    }
                }

                var client = _rt._httpClientFactory.CreateClient(LoopbackClientName);
                return Send(client, request);
            }

            // ---- cms.http ----
            public string Http(string method, string url, string? optionsJson)
            {
                if (++_httpCalls > MaxHttpCalls)
                    return Error($"cms.http call limit ({MaxHttpCalls}) per run exceeded.");
                if (method is not ("GET" or "POST" or "PUT" or "DELETE" or "PATCH" or "HEAD"))
                    return Error($"Unsupported HTTP method '{method}'.");
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    return Error("cms.http requires an absolute http(s) URL.");

                var options = ParseObject(optionsJson);
                using var request = new HttpRequestMessage(new HttpMethod(method), uri);

                var body = options["body"];
                HttpContent? content = null;
                if (body != null)
                {
                    content = body is JsonValue v && v.TryGetValue<string>(out var s)
                        ? new StringContent(s, Encoding.UTF8, "text/plain")
                        : new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                    request.Content = content;
                }

                if (options["headers"] is JsonObject headers)
                {
                    foreach (var h in headers)
                    {
                        var value = h.Value?.ToString() ?? string.Empty;
                        if (h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!request.Headers.TryAddWithoutValidation(h.Key, value))
                            request.Content?.Headers.TryAddWithoutValidation(h.Key, value);
                    }
                }

                var client = _rt._httpClientFactory.CreateClient(ExternalClientName);
                return Send(client, request);
            }

            private string Send(HttpClient client, HttpRequestMessage request)
            {
                try
                {
                    using var response = client.Send(request, HttpCompletionOption.ResponseHeadersRead, _ct);
                    using var stream = response.Content.ReadAsStream(_ct);
                    using var buffer = new MemoryStream();
                    var chunk = new byte[8192];
                    int read;
                    while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        buffer.Write(chunk, 0, read);
                        if (buffer.Length > MaxResponseBytes)
                            return Error($"Response is larger than {MaxResponseBytes / 1024} KB.");
                    }

                    var text = Encoding.UTF8.GetString(buffer.ToArray());
                    JsonNode? bodyNode;
                    try { bodyNode = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text); }
                    catch (JsonException) { bodyNode = text; }

                    var headers = new JsonObject();
                    foreach (var h in response.Headers.Concat(response.Content.Headers))
                        headers[h.Key.ToLowerInvariant()] = string.Join(", ", h.Value);

                    return new JsonObject
                    {
                        ["response"] = new JsonObject
                        {
                            ["status"] = (int)response.StatusCode,
                            ["ok"] = response.IsSuccessStatusCode,
                            ["headers"] = headers,
                            ["body"] = bodyNode
                        }
                    }.ToJsonString();
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
                {
                    return Error($"Request failed: {ex.Message}");
                }
            }

            // ---- cms.storage ----
            public string Storage(string operation, string key, string? valueJson)
            {
                if (++_storageOps > MaxStorageOps)
                    return Error($"cms.storage operation limit ({MaxStorageOps}) per run exceeded.");
                if (operation != "list" && (key.Length == 0 || key.Length > 100))
                    return Error("Storage keys must be 1-100 characters.");

                var db = _rt._context;
                switch (operation)
                {
                    case "get":
                    {
                        var entry = db.PluginStorageEntries.AsNoTracking()
                            .FirstOrDefault(e => e.PluginId == _pluginId && e.UserId == _userId && e.Key == key);
                        return "{\"response\":" + (entry?.Value ?? "null") + "}";
                    }
                    case "set":
                    {
                        var value = valueJson ?? "null";
                        if (Encoding.UTF8.GetByteCount(value) > MaxStorageValueBytes)
                            return Error($"Storage values are limited to {MaxStorageValueBytes / 1024} KB.");

                        var entry = db.PluginStorageEntries
                            .FirstOrDefault(e => e.PluginId == _pluginId && e.UserId == _userId && e.Key == key);
                        if (entry == null)
                        {
                            if (db.PluginStorageEntries.Count(e => e.PluginId == _pluginId && e.UserId == _userId) >= MaxStorageEntries)
                                return Error($"Storage is limited to {MaxStorageEntries} keys per user.");
                            entry = new PluginStorageEntry { PluginId = _pluginId, UserId = _userId, Key = key };
                            db.PluginStorageEntries.Add(entry);
                        }
                        entry.Value = value;
                        entry.UpdatedAt = DateTime.UtcNow;
                        db.SaveChanges();
                        return "{\"response\":true}";
                    }
                    case "remove":
                    {
                        db.PluginStorageEntries
                            .Where(e => e.PluginId == _pluginId && e.UserId == _userId && e.Key == key)
                            .ExecuteDelete();
                        return "{\"response\":true}";
                    }
                    case "list":
                    {
                        var keys = db.PluginStorageEntries.AsNoTracking()
                            .Where(e => e.PluginId == _pluginId && e.UserId == _userId && e.Key.StartsWith(key))
                            .OrderBy(e => e.Key)
                            .Select(e => e.Key)
                            .ToList();
                        return new JsonObject { ["response"] = new JsonArray(keys.Select(k => (JsonNode?)k).ToArray()) }.ToJsonString();
                    }
                    default:
                        return Error($"Unknown storage operation '{operation}'.");
                }
            }

            private static string FormValue(JsonNode? node) => node switch
            {
                null => string.Empty,
                JsonValue v when v.TryGetValue<string>(out var s) => s,
                JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
                _ => node.ToJsonString()
            };

            private static JsonObject ParseObject(string? json)
            {
                if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
                try { return JsonNode.Parse(json) as JsonObject ?? new JsonObject(); }
                catch (JsonException) { return new JsonObject(); }
            }

            private static string Error(string message) =>
                new JsonObject { ["error"] = message }.ToJsonString();
        }
    }
}
