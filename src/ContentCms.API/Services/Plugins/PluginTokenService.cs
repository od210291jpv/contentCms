using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ContentCms.API.Services.Plugins
{
    public record PluginTokenInfo(int UserId, int PluginId, DateTime ExpiresAtUtc);

    public interface IPluginTokenService
    {
        /// <summary>Issues a short-lived token that authenticates API calls as <paramref name="userId"/> on behalf of a plugin.</summary>
        string Issue(int userId, int pluginId, TimeSpan lifetime);

        /// <summary>Returns token details when the token is a valid, non-expired plugin token; otherwise null.</summary>
        PluginTokenInfo? Validate(string? token);

        void Revoke(string token);
    }

    /// <summary>
    /// In-memory store of scoped plugin tokens. Tokens are random, opaque, short-lived and are never
    /// accepted by the plugin management endpoints, so plugins cannot manage plugins.
    /// </summary>
    public class PluginTokenService : IPluginTokenService
    {
        public const string Prefix = "plg_";
        private readonly ConcurrentDictionary<string, PluginTokenInfo> _tokens = new();

        public string Issue(int userId, int pluginId, TimeSpan lifetime)
        {
            Purge();
            var token = Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            _tokens[token] = new PluginTokenInfo(userId, pluginId, DateTime.UtcNow.Add(lifetime));
            return token;
        }

        public PluginTokenInfo? Validate(string? token)
        {
            if (string.IsNullOrEmpty(token) || !token.StartsWith(Prefix, StringComparison.Ordinal))
                return null;

            if (!_tokens.TryGetValue(token, out var info))
                return null;

            if (info.ExpiresAtUtc <= DateTime.UtcNow)
            {
                _tokens.TryRemove(token, out _);
                return null;
            }

            return info;
        }

        public void Revoke(string token) => _tokens.TryRemove(token, out _);

        private void Purge()
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _tokens)
            {
                if (kv.Value.ExpiresAtUtc <= now)
                    _tokens.TryRemove(kv.Key, out _);
            }
        }
    }
}
