using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContentCms.API.Models;

namespace ContentCms.API.Services.Plugins
{
    /// <summary>A UI plugin ready to be rendered in a sandboxed iframe.</summary>
    public class PluginWidget
    {
        public required int PluginId { get; init; }
        public required string Name { get; init; }
        public required string SrcDoc { get; init; }
        /// <summary>JSON sent to the iframe once it announces itself: plugin, user and config.</summary>
        public required string InitJson { get; init; }
    }

    public class PluginWidgetsModel
    {
        public string Slot { get; init; } = string.Empty;
        public List<PluginWidget> Widgets { get; init; } = new();
    }

    /// <summary>
    /// Builds the sandboxed iframe documents for UI plugins. The iframe has no same-origin access
    /// (<c>sandbox="allow-scripts"</c>) and talks to the page only through postMessage; the host page
    /// (plugin-host.js) performs API calls with a scoped token on its behalf.
    /// </summary>
    public static class PluginUiHost
    {
        public static async Task<PluginWidgetsModel> BuildWidgetsAsync(IPluginService plugins, ClaimsPrincipal principal, string slot)
        {
            var model = new PluginWidgetsModel { Slot = slot };
            if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return model;

            var username = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
            var role = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

            foreach (var view in await plugins.GetUiWidgetsAsync(userId, slot))
            {
                var init = new JsonObject
                {
                    ["plugin"] = new JsonObject { ["id"] = view.Plugin.Id, ["key"] = view.Plugin.Key, ["name"] = view.Plugin.Name },
                    ["user"] = new JsonObject { ["id"] = userId, ["username"] = username, ["role"] = role },
                    // Secrets are masked: widgets run in the browser and must never receive them.
                    ["config"] = view.Config.DeepClone()
                };

                model.Widgets.Add(new PluginWidget
                {
                    PluginId = view.Plugin.Id,
                    Name = view.Plugin.Name,
                    SrcDoc = BuildSrcDoc(view.Plugin.UiHtml ?? string.Empty),
                    InitJson = init.ToJsonString()
                });
            }
            return model;
        }

        public static string BuildSrcDoc(string uiHtml) =>
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
            "<style>html,body{margin:0;padding:0;font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;font-size:14px;color:#212529}</style>" +
            "<script>" + Bridge + "</script></head><body>" + uiHtml + "</body></html>";

        /// <summary>Script injected into every UI plugin: defines <c>window.cms</c> inside the iframe.</summary>
        private const string Bridge = """
(function () {
  var pending = {}, seq = 0, readyResolve;
  function post(msg) { parent.postMessage(msg, '*'); }
  var cms = window.cms = {
    plugin: null, user: null, config: {},
    ready: new Promise(function (r) { readyResolve = r; }),
    api: function (method, path, body, opts) {
      return new Promise(function (resolve, reject) {
        var id = ++seq;
        pending[id] = { resolve: resolve, reject: reject };
        post({ type: 'api', id: id, method: String(method).toUpperCase(), path: String(path), body: body, form: !!(opts && opts.form), multipart: !!(opts && opts.multipart) });
      });
    },
    get: function (path) { return cms.api('GET', path); },
    post: function (path, body, opts) { return cms.api('POST', path, body, opts); },
    put: function (path, body, opts) { return cms.api('PUT', path, body, opts); },
    delete: function (path) { return cms.api('DELETE', path); },
    resize: function (height) { post({ type: 'resize', height: height }); }
  };
  window.addEventListener('message', function (e) {
    if (e.source !== parent || !e.data || typeof e.data !== 'object') return;
    var m = e.data;
    if (m.type === 'init') {
      cms.plugin = m.plugin; cms.user = m.user; cms.config = m.config || {};
      readyResolve(cms);
      document.dispatchEvent(new CustomEvent('cms:ready', { detail: cms }));
    } else if (m.type === 'api-result' && pending[m.id]) {
      var p = pending[m.id]; delete pending[m.id];
      if (m.error) p.reject(new Error(m.error)); else p.resolve({ status: m.status, ok: m.ok, body: m.body });
    }
  });
  function autoResize() { post({ type: 'resize', height: Math.ceil(document.documentElement.scrollHeight) }); }
  if (window.ResizeObserver) new ResizeObserver(autoResize).observe(document.documentElement);
  window.addEventListener('load', autoResize);
  post({ type: 'hello' });
})();
""";
    }
}
