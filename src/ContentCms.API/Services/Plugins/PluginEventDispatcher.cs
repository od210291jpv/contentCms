using System.Collections.Concurrent;

namespace ContentCms.API.Services.Plugins
{
    /// <summary>
    /// Consumes content events from <see cref="PluginEventQueue"/> and runs the backend plugins that are
    /// subscribed and enabled for the affected user(s).
    /// </summary>
    public class PluginEventDispatcher : BackgroundService
    {
        private const int MaxRunsPerMinutePerPlugin = 120;

        private readonly PluginEventQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PluginEventDispatcher> _logger;
        private readonly ConcurrentDictionary<int, Queue<DateTime>> _runHistory = new();

        public PluginEventDispatcher(
            PluginEventQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<PluginEventDispatcher> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var pluginEvent in _queue.Channel.Reader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        await DispatchAsync(pluginEvent, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Plugin dispatch failed for event {Event} (content {ContentId}).",
                            pluginEvent.Name, pluginEvent.Content.Id);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("PluginEventDispatcher is stopping.");
            }
        }

        private async Task DispatchAsync(PluginEvent pluginEvent, CancellationToken ct)
        {
            List<PluginRunTarget> targets;
            using (var scope = _scopeFactory.CreateScope())
            {
                var plugins = scope.ServiceProvider.GetRequiredService<IPluginService>();
                targets = await plugins.GetRunTargetsAsync(pluginEvent.Name, pluginEvent.Content.OwnerId);
            }

            foreach (var target in targets)
            {
                // Loop guard: a plugin is never triggered by changes it made itself.
                if (pluginEvent.OriginPluginId == target.Plugin.Id)
                    continue;

                if (!TryAcquireRunSlot(target.Plugin.Id))
                {
                    _logger.LogWarning("Plugin {Plugin} exceeded {Max} runs/minute; event {Event} skipped.",
                        target.Plugin.Key, MaxRunsPerMinutePerPlugin, pluginEvent.Name);
                    continue;
                }

                // A fresh scope (and DbContext) per run keeps runs isolated from each other.
                using var runScope = _scopeFactory.CreateScope();
                var runtime = runScope.ServiceProvider.GetRequiredService<IPluginRuntime>();
                await runtime.RunEventAsync(target, pluginEvent, ct);
            }
        }

        private bool TryAcquireRunSlot(int pluginId)
        {
            var history = _runHistory.GetOrAdd(pluginId, _ => new Queue<DateTime>());
            var now = DateTime.UtcNow;
            lock (history)
            {
                while (history.Count > 0 && now - history.Peek() > TimeSpan.FromMinutes(1))
                    history.Dequeue();
                if (history.Count >= MaxRunsPerMinutePerPlugin)
                    return false;
                history.Enqueue(now);
                return true;
            }
        }
    }
}
