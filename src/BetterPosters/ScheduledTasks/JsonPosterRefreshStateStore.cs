using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.BetterPosters.ScheduledTasks;

/// <summary>
/// Persists per-item Better Posters URL fingerprints in the Jellyfin plugin configurations directory using atomic file replacement.
/// </summary>
public sealed class JsonPosterRefreshStateStore : IPosterRefreshStateStore
{
    private const string StateFileName = "Jellyfin.Plugin.BetterPosters.RefreshState.json";
    private readonly string _stateFilePath;
    private readonly ILogger<JsonPosterRefreshStateStore> _logger;
    private readonly ConcurrentDictionary<Guid, string> _fingerprints = new();
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private volatile bool _loaded;
    private volatile bool _dirty;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonPosterRefreshStateStore"/> class.
    /// </summary>
    public JsonPosterRefreshStateStore(
        IApplicationPaths applicationPaths,
        ILogger<JsonPosterRefreshStateStore> logger)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        ArgumentNullException.ThrowIfNull(logger);

        _stateFilePath = Path.Combine(applicationPaths.PluginConfigurationsPath, StateFileName);
        _logger = logger;
    }

    /// <inheritdoc />
    public bool TryGetRecordedFingerprint(Guid itemId, out string fingerprint)
    {
        EnsureLoaded();
        return _fingerprints.TryGetValue(itemId, out fingerprint!);
    }

    /// <inheritdoc />
    public void RecordFingerprint(Guid itemId, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        EnsureLoaded();
        _fingerprints[itemId] = fingerprint;
        _dirty = true;
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (!_dirty)
        {
            return;
        }

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_dirty)
            {
                return;
            }

            var directory = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var snapshot = new Dictionary<string, string>(_fingerprints.Count, StringComparer.Ordinal);
            foreach (var kvp in _fingerprints)
            {
                snapshot[kvp.Key.ToString("N")] = kvp.Value;
            }

            var tempPath = _stateFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot);
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, _stateFilePath, overwrite: true);
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to persist Better Posters refresh fingerprint state (reason={ReasonCode}).", "state_persist_failed");
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _ioLock.Wait();
        try
        {
            if (_loaded)
            {
                return;
            }

            if (File.Exists(_stateFilePath))
            {
                var bytes = File.ReadAllBytes(_stateFilePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(bytes);
                if (data is not null)
                {
                    foreach (var kvp in data)
                    {
                        if (Guid.TryParse(kvp.Key, out var id) && !string.IsNullOrWhiteSpace(kvp.Value))
                        {
                            _fingerprints[id] = kvp.Value;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to read existing Better Posters refresh state; starting fresh (reason={ReasonCode}).", "state_load_failed");
        }
        finally
        {
            _loaded = true;
            _ioLock.Release();
        }
    }
}
