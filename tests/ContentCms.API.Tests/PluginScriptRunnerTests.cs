using System.Text.Json.Nodes;
using ContentCms.API.Services.Plugins;
using Xunit;

namespace ContentCms.API.Tests
{
    public class PluginScriptRunnerTests
    {
        private class FakeHost : IPluginHost
        {
            public List<(string Method, string Path, string? Body)> ApiCalls { get; } = new();
            public List<(string Level, string Message)> Logs { get; } = new();
            public Dictionary<string, string> Store { get; } = new();

            public string Api(string method, string path, string? bodyJson)
            {
                ApiCalls.Add((method, path, bodyJson));
                return "{\"response\":{\"status\":200,\"ok\":true,\"body\":{\"hello\":\"world\"}}}";
            }

            public string Http(string method, string url, string? optionsJson) =>
                "{\"error\":\"http disabled in test\"}";

            public string Storage(string operation, string key, string? valueJson)
            {
                switch (operation)
                {
                    case "set": Store[key] = valueJson ?? "null"; return "{\"response\":true}";
                    case "get":
                        return Store.TryGetValue(key, out var v)
                            ? "{\"response\":" + v + "}"
                            : "{\"response\":null}";
                    default: return "{\"response\":null}";
                }
            }

            public void Log(string level, string message) => Logs.Add((level, message));
        }

        private static PluginRunContext Ctx(JsonObject? config = null) => new()
        {
            PluginId = 1,
            PluginKey = "test",
            PluginName = "Test",
            PluginVersion = "1.0.0",
            UserId = 7,
            Username = "alice",
            Role = "User",
            Trigger = "content.created",
            Config = config ?? new JsonObject()
        };

        private static PluginRunResult Run(string script, FakeHost host, string name = "content.created",
            string json = "{\"type\":\"content.created\",\"id\":42}", JsonObject? config = null,
            PluginRunLimits? limits = null) =>
            PluginScriptRunner.Run(script, Ctx(config), name, json, host, limits);

        [Fact]
        public void Handler_receives_event_and_can_call_api_and_log()
        {
            var host = new FakeHost();
            var result = Run(@"
                cms.on('content.created', function (ev) {
                    var r = cms.api.get('/api/content/' + ev.id);
                    cms.log.info('status=' + r.status + ' hello=' + r.body.hello + ' user=' + cms.user.username);
                });", host);

            Assert.True(result.Success, result.Error);
            Assert.Equal(1, result.HandlersInvoked);
            Assert.Equal(("GET", "/api/content/42", (string?)null), host.ApiCalls.Single());
            Assert.Contains(host.Logs, l => l.Message == "status=200 hello=world user=alice");
        }

        [Fact]
        public void Only_matching_handlers_and_wildcards_run()
        {
            var host = new FakeHost();
            var result = Run(@"
                cms.on('content.deleted', function () { cms.log.info('deleted'); });
                cms.on('*', function () { cms.log.info('any'); });", host);

            Assert.True(result.Success, result.Error);
            Assert.Equal(1, result.HandlersInvoked);
            Assert.DoesNotContain(host.Logs, l => l.Message == "deleted");
            Assert.Contains(host.Logs, l => l.Message == "any");
        }

        [Fact]
        public void Config_is_exposed_and_frozen()
        {
            var host = new FakeHost();
            var result = Run(@"
                cms.on('content.created', function () {
                    cms.log.info(cms.config.url);
                    try { cms.config.url = 'x'; } catch (e) { cms.log.info('frozen'); }
                });", host, config: new JsonObject { ["url"] = "https://example.test" });

            Assert.True(result.Success, result.Error);
            Assert.Contains(host.Logs, l => l.Message == "https://example.test");
            Assert.Contains(host.Logs, l => l.Message == "frozen");
        }

        [Fact]
        public void Storage_roundtrips_json_values()
        {
            var host = new FakeHost();
            var result = Run(@"
                cms.on('content.created', function () {
                    cms.storage.set('counter', { n: 5 });
                    cms.log.info('n=' + cms.storage.get('counter').n);
                });", host);

            Assert.True(result.Success, result.Error);
            Assert.Contains(host.Logs, l => l.Message == "n=5");
        }

        [Fact]
        public void Host_errors_surface_as_script_exceptions()
        {
            var host = new FakeHost();
            var result = Run(@"
                cms.on('content.created', function () { cms.http.get('http://example.test'); });", host);

            Assert.False(result.Success);
            Assert.Contains("http disabled in test", result.Error);
        }

        [Fact]
        public void Syntax_errors_are_reported()
        {
            var result = Run("cms.on('content.created', function () {", new FakeHost());
            Assert.False(result.Success);
            Assert.Throws<Jint.ScriptPreparationException>(() => PluginScriptRunner.ValidateSyntax("function ("));
        }

        [Fact]
        public void Infinite_loop_is_stopped()
        {
            var limits = new PluginRunLimits { Timeout = TimeSpan.FromMilliseconds(300), MaxStatements = 100_000_000 };
            var result = Run("cms.on('content.created', function () { while (true) {} });", new FakeHost(), limits: limits);

            Assert.False(result.Success);
        }

        [Fact]
        public void Statement_budget_is_enforced()
        {
            var limits = new PluginRunLimits { Timeout = TimeSpan.FromSeconds(30), MaxStatements = 1000 };
            var result = Run("cms.on('content.created', function () { var i = 0; while (true) { i++; } });", new FakeHost(), limits: limits);

            Assert.False(result.Success);
            Assert.Contains("statements", result.Error);
        }

        [Fact]
        public void Clr_and_host_internals_are_not_reachable()
        {
            var host = new FakeHost();
            var result = Run(@"
                cms.on('content.created', function () {
                    cms.log.info('clr=' + typeof System + ' api=' + typeof __api + ' ctx=' + typeof __ctx);
                });", host);

            Assert.True(result.Success, result.Error);
            Assert.Contains(host.Logs, l => l.Message == "clr=undefined api=undefined ctx=undefined");
        }

        [Fact]
        public void Handler_exceptions_are_reported_not_thrown()
        {
            var result = Run("cms.on('content.created', function () { throw new Error('boom'); });", new FakeHost());
            Assert.False(result.Success);
            Assert.Contains("boom", result.Error);
        }
    }
}
