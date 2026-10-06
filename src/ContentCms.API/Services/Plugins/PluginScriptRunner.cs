using System.Text.Json;
using System.Text.Json.Nodes;
using Jint;
using Jint.Native;

namespace ContentCms.API.Services.Plugins
{
    /// <summary>
    /// Capabilities the runtime offers to a plugin script. Every method receives and returns plain strings
    /// (JSON), so no .NET object is ever exposed to the sandboxed script.
    /// Implementations return JSON: <c>{"response": ...}</c> on success or <c>{"error": "message"}</c>.
    /// </summary>
    public interface IPluginHost
    {
        string Api(string method, string path, string? bodyJson);
        string Http(string method, string url, string? optionsJson);
        string Storage(string operation, string key, string? valueJson);
        void Log(string level, string message);
    }

    public class PluginRunContext
    {
        public int PluginId { get; init; }
        public string PluginKey { get; init; } = string.Empty;
        public string PluginName { get; init; } = string.Empty;
        public string PluginVersion { get; init; } = string.Empty;
        public int UserId { get; init; }
        public string Username { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public string Trigger { get; init; } = string.Empty;
        public JsonObject Config { get; init; } = new();
    }

    public class PluginRunLimits
    {
        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
        public long MemoryBytes { get; init; } = 32 * 1024 * 1024;
        public int MaxRecursion { get; init; } = 100;
        public int MaxStatements { get; init; } = 2_000_000;
    }

    public record PluginRunResult(bool Success, int HandlersInvoked, string? Error);

    /// <summary>Executes plugin scripts inside a Jint sandbox (no CLR access, hard CPU/memory limits).</summary>
    public static class PluginScriptRunner
    {
        /// <summary>Throws when <paramref name="script"/> cannot be parsed.</summary>
        public static void ValidateSyntax(string script)
        {
            Engine.PrepareScript(script, null, strict: true);
        }

        public static PluginRunResult Run(
            string script,
            PluginRunContext context,
            string eventName,
            string eventJson,
            IPluginHost host,
            PluginRunLimits? limits = null,
            CancellationToken cancellationToken = default)
        {
            limits ??= new PluginRunLimits();

            try
            {
                var engine = new Engine(options =>
                {
                    options.Strict();
                    options.TimeoutInterval(limits.Timeout);
                    options.LimitMemory(limits.MemoryBytes);
                    options.LimitRecursion(limits.MaxRecursion);
                    options.MaxStatements(limits.MaxStatements);
                    options.CancellationToken(cancellationToken);
                });

                engine.SetValue("__api", new Func<string, string, string?, string>((m, p, b) => Safe(() => host.Api(m, p, b))));
                engine.SetValue("__http", new Func<string, string, string?, string>((m, u, o) => Safe(() => host.Http(m, u, o))));
                engine.SetValue("__storage", new Func<string, string, string?, string>((op, k, v) => Safe(() => host.Storage(op, k, v))));
                engine.SetValue("__log", new Action<string, string>((l, m) =>
                {
                    try { host.Log(l, m); } catch { /* logging must never break a script */ }
                }));
                engine.SetValue("__ctx", BuildContextJson(context));

                var dispatch = engine.Evaluate(Bootstrap);

                engine.Execute(script);

                var count = engine.Invoke(dispatch, JsValue.Undefined, new JsValue[] { eventName, eventJson });
                return new PluginRunResult(true, (int)count.AsNumber(), null);
            }
            catch (Jint.Runtime.JavaScriptException ex)
            {
                return new PluginRunResult(false, 0, $"Script error: {ex.Message}");
            }
            catch (TimeoutException)
            {
                return new PluginRunResult(false, 0, $"Script exceeded the time limit of {limits.Timeout.TotalSeconds:0.#}s.");
            }
            catch (Jint.Runtime.MemoryLimitExceededException)
            {
                return new PluginRunResult(false, 0, "Script exceeded the memory limit.");
            }
            catch (Jint.Runtime.StatementsCountOverflowException)
            {
                return new PluginRunResult(false, 0, "Script exceeded the maximum number of statements.");
            }
            catch (Jint.Runtime.RecursionDepthOverflowException)
            {
                return new PluginRunResult(false, 0, "Script exceeded the maximum call depth.");
            }
            catch (OperationCanceledException)
            {
                return new PluginRunResult(false, 0, "Script run was cancelled.");
            }
            catch (Exception ex)
            {
                return new PluginRunResult(false, 0, $"Script failed: {ex.Message}");
            }
        }

        private static string Safe(Func<string> action)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                return new JsonObject { ["error"] = ex.Message }.ToJsonString();
            }
        }

        private static string BuildContextJson(PluginRunContext c) => new JsonObject
        {
            ["plugin"] = new JsonObject
            {
                ["id"] = c.PluginId,
                ["key"] = c.PluginKey,
                ["name"] = c.PluginName,
                ["version"] = c.PluginVersion
            },
            ["user"] = new JsonObject
            {
                ["id"] = c.UserId,
                ["username"] = c.Username,
                ["role"] = c.Role
            },
            ["trigger"] = c.Trigger,
            ["config"] = c.Config.DeepClone()
        }.ToJsonString(new JsonSerializerOptions());

        /// <summary>
        /// Builds the global <c>cms</c> object and returns the (tamper-proof) dispatch function.
        /// Host functions are captured in a closure and removed from the global scope.
        /// </summary>
        private const string Bootstrap = """
(function (hostApi, hostHttp, hostStorage, hostLog, ctxJson) {
  'use strict';
  var ctx = JSON.parse(ctxJson);
  var handlers = Object.create(null);

  function fmt(a) {
    if (typeof a === 'string') return a;
    try { return JSON.stringify(a); } catch (e) { return String(a); }
  }
  function logger(level) {
    return function () { hostLog(level, Array.prototype.map.call(arguments, fmt).join(' ')); };
  }
  function unwrap(json) {
    var r = JSON.parse(json);
    if (r && r.error) throw new Error(r.error);
    return r ? r.response : undefined;
  }
  function body(b) { return b === undefined ? null : JSON.stringify(b); }

  var log = { debug: logger('debug'), info: logger('info'), warn: logger('warn'), error: logger('error') };

  var cms = {
    plugin: ctx.plugin,
    user: ctx.user,
    trigger: ctx.trigger,
    config: Object.freeze(ctx.config),
    on: function (name, fn) {
      if (typeof name !== 'string' || typeof fn !== 'function') throw new Error('cms.on(eventName, handler) expects a string and a function');
      (handlers[name] = handlers[name] || []).push(fn);
    },
    log: log,
    api: {
      request: function (method, path, b, opts) {
        return unwrap(hostApi(String(method).toUpperCase(), String(path), JSON.stringify({ body: b === undefined ? null : b, form: !!(opts && opts.form) })));
      },
      get: function (path) { return this.request('GET', path); },
      post: function (path, b, opts) { return this.request('POST', path, b, opts); },
      put: function (path, b, opts) { return this.request('PUT', path, b, opts); },
      delete: function (path) { return this.request('DELETE', path); }
    },
    http: {
      request: function (method, url, options) { return unwrap(hostHttp(String(method).toUpperCase(), String(url), body(options))); },
      get: function (url, options) { return this.request('GET', url, options); },
      post: function (url, b, options) {
        var o = options || {};
        o.body = b;
        return this.request('POST', url, o);
      }
    },
    storage: {
      get: function (key) { return unwrap(hostStorage('get', String(key), null)); },
      set: function (key, value) { unwrap(hostStorage('set', String(key), JSON.stringify(value === undefined ? null : value))); },
      remove: function (key) { unwrap(hostStorage('remove', String(key), null)); },
      list: function (prefix) { return unwrap(hostStorage('list', prefix === undefined ? '' : String(prefix), null)); }
    }
  };

  globalThis.cms = cms;
  globalThis.console = { log: log.info, info: log.info, warn: log.warn, error: log.error, debug: log.debug };
  delete globalThis.__api; delete globalThis.__http; delete globalThis.__storage; delete globalThis.__log; delete globalThis.__ctx;

  return function dispatch(name, json) {
    var ev = JSON.parse(json);
    var list = (handlers[name] || []).concat(handlers['*'] || []);
    for (var i = 0; i < list.length; i++) list[i](ev, cms);
    return list.length;
  };
})(__api, __http, __storage, __log, __ctx)
""";
    }
}
