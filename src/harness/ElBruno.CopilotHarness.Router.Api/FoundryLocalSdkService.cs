using Microsoft.AI.Foundry.Local;

namespace ElBruno.CopilotHarness.Router.Api;

/// <summary>
/// Singleton wrapper around <see cref="FoundryLocalManager"/>.
/// Manages lazy initialization of the Foundry Local SDK, exposes the model catalog,
/// and tracks in-progress model downloads with per-alias progress callbacks.
/// </summary>
public sealed class FoundryLocalSdkService(
    ILogger<FoundryLocalSdkService> logger,
    IHostApplicationLifetime lifetime) : IDisposable
{
    // ── State ────────────────────────────────────────────────────────────────

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private string? _webServiceUrl;
    private Exception? _initError;

    // alias → 0-100 progress
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, float> _downloadProgress
        = new(StringComparer.OrdinalIgnoreCase);

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>True after <see cref="EnsureInitializedAsync"/> completed without error.</summary>
    public bool IsInitialized => _initialized;

    /// <summary>The URL where the Foundry Local web service is listening, or null if not started.</summary>
    public string? WebServiceUrl => _webServiceUrl;

    /// <summary>The last initialization error, if any.</summary>
    public string? InitError => _initError?.Message;

    /// <summary>Returns the current download progress for <paramref name="alias"/> (0–100), or null if not downloading.</summary>
    public float? GetDownloadProgress(string alias)
        => _downloadProgress.TryGetValue(alias, out var p) ? p : null;

    /// <summary>
    /// Initializes the Foundry Local SDK singleton on first call.
    /// Subsequent calls are no-ops (idempotent).
    /// </summary>
    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            logger.LogInformation("Initializing Foundry Local SDK...");
            await FoundryLocalManager.CreateAsync(
                new Configuration { AppName = "CopilotHarness" },
                logger).ConfigureAwait(false);

            // Start the SDK-embedded OpenAI-compatible web service so existing
            // FoundryLocalChatCompletionsProvider can reach it via HTTP.
            await FoundryLocalManager.Instance.StartWebServiceAsync().ConfigureAwait(false);
            _webServiceUrl = FoundryLocalManager.Instance.Urls?.FirstOrDefault();
            _initialized = true;
            _initError = null;

            logger.LogInformation("Foundry Local SDK initialized. Web service listening at {Url}.", _webServiceUrl);
        }
        catch (Exception ex)
        {
            _initError = ex;
            logger.LogError(ex, "Failed to initialize Foundry Local SDK.");
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>Lists all models available in the Foundry Local catalog.</summary>
    public async Task<IReadOnlyList<FoundryCatalogModelInfo>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (!_initialized) return [];

        var catalog = await FoundryLocalManager.Instance.GetCatalogAsync().ConfigureAwait(false);
        var models = await catalog.ListModelsAsync().ConfigureAwait(false);
        var cached = await catalog.GetCachedModelsAsync().ConfigureAwait(false);
        var loaded = await catalog.GetLoadedModelsAsync().ConfigureAwait(false);

        var cachedAliases = cached.Select(m => m.Alias).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loadedAliases = loaded.Select(m => m.Alias).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return models.Select(m => new FoundryCatalogModelInfo(
            Alias: m.Alias,
            DisplayName: m.Info?.DisplayName ?? m.Alias,
            Description: null,
            ModelId: m.Id,
            IsCached: cachedAliases.Contains(m.Alias),
            IsLoaded: loadedAliases.Contains(m.Alias),
            DownloadProgress: GetDownloadProgress(m.Alias)
        )).ToList();
    }

    /// <summary>
    /// Downloads the model identified by <paramref name="alias"/> from the Foundry Local catalog.
    /// Progress is tracked and queryable via <see cref="GetDownloadProgress"/>.
    /// </summary>
    public async Task DownloadModelAsync(string alias, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        if (!_initialized) throw new InvalidOperationException("Foundry Local SDK is not initialized.");

        var catalog = await FoundryLocalManager.Instance.GetCatalogAsync().ConfigureAwait(false);
        var model = await catalog.GetModelAsync(alias).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Model '{alias}' not found in Foundry Local catalog.");

        _downloadProgress[alias] = 0f;
        try
        {
            await model.DownloadAsync(
                progress =>
                {
                    _downloadProgress[alias] = progress;
                    logger.LogDebug("Downloading {Alias}: {Progress:F1}%", alias, progress);
                },
                cancellationToken).ConfigureAwait(false);

            _downloadProgress[alias] = 100f;
            logger.LogInformation("Model '{Alias}' downloaded successfully.", alias);
        }
        catch
        {
            _downloadProgress.TryRemove(alias, out _);
            throw;
        }
    }

    public void Dispose()
    {
        if (_initialized && FoundryLocalManager.IsInitialized)
        {
            try { FoundryLocalManager.Instance.Dispose(); }
            catch { /* best-effort */ }
        }
        _initLock.Dispose();
    }
}

/// <summary>Model entry from the Foundry Local catalog (read-only projection).</summary>
public sealed record FoundryCatalogModelInfo(
    string Alias,
    string DisplayName,
    string? Description,
    string ModelId,
    bool IsCached,
    bool IsLoaded,
    float? DownloadProgress);
