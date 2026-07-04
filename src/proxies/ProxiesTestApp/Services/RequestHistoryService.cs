namespace ProxiesTestApp.Services;

// =============================================================================
//  RequestHistoryService — singleton, tracks the last N LLM requests across
//  all proxies and pages.  Used by History.razor and the request counter badge.
// =============================================================================

public sealed record RequestHistoryEntry(
    string          Proxy,
    string          Model,
    string          PromptPreview,   // first 80 chars of the user message
    bool            Streaming,
    long            LatencyMs,
    int             TokensEstimate,  // number of streaming chunks ≈ tokens
    string?         Error,
    DateTimeOffset  Timestamp
);

public sealed class RequestHistoryService
{
    private readonly List<RequestHistoryEntry> _entries = [];
    private readonly Lock _lock = new();
    private const int MaxEntries = 100;

    public void Add(RequestHistoryEntry entry)
    {
        lock (_lock)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveAt(_entries.Count - 1);
        }
    }

    public IReadOnlyList<RequestHistoryEntry> GetAll()
    {
        lock (_lock) return _entries.ToList();
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }
}
