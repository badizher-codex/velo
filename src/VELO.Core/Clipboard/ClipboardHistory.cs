namespace VELO.Core.Clipboard;

/// <summary>
/// Phase 3 / Sprint 9D (v2.4.23) — In-memory ring buffer of recent text-clipboard
/// entries. Privacy-first by construction:
///
///   • <b>No disk persistence.</b> Entries live in process memory only and are
///     dropped when VELO closes — same model as Chrome's incognito clipboard
///     history but always-on.
///   • <b>Text only.</b> File and image clipboards are ignored; we never inspect
///     binary payloads.
///   • <b>Password heuristic.</b> Entries that look like a generated password
///     (length 12-64, mix of upper+lower+digit, no whitespace) are tagged so
///     UI can flag or skip them.
///   • <b>Bounded.</b> Cap at <see cref="MaxEntries"/>; oldest entries fall
///     off when full.
///   • <b>Deduped.</b> Identical consecutive captures collapse to a single
///     entry (no spam when the user copies the same string twice).
///
/// The class itself is pure (no WPF, no clipboard API). The polling loop that
/// reads <c>System.Windows.Clipboard</c> lives in the host UI layer; it calls
/// <see cref="TryAdd"/> with the latest text. Tests in
/// <c>ClipboardHistoryTests</c>.
/// </summary>
public sealed class ClipboardHistory
{
    public sealed record Entry(
        string Text,
        DateTime CapturedAtUtc,
        bool LooksLikePassword);

    /// <summary>Maximum entries retained. Older ones fall off when full.</summary>
    public int MaxEntries { get; init; } = 20;

    /// <summary>Raised after a successful <see cref="TryAdd"/>. UI subscribes to refresh.</summary>
    public event Action<Entry>? EntryAdded;

    private readonly LinkedList<Entry> _entries = new();
    private readonly object _lock = new();

    /// <summary>
    /// Tries to add a clipboard text capture. Returns true when the entry
    /// was accepted, false when it was rejected (empty, identical to the
    /// most-recent entry, or whitespace-only).
    /// </summary>
    public bool TryAdd(string text, DateTime? atUtc = null)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var now = atUtc ?? DateTime.UtcNow;
        Entry entry;
        lock (_lock)
        {
            // Dedup: identical to the most-recent entry → skip.
            if (_entries.First is { Value.Text: var prevText } && prevText == text)
                return false;

            entry = new Entry(text, now, LooksLikePassword(text));
            _entries.AddFirst(entry);
            while (_entries.Count > MaxEntries) _entries.RemoveLast();
        }

        try { EntryAdded?.Invoke(entry); }
        catch { /* subscriber threw — don't poison the captor */ }
        return true;
    }

    /// <summary>Returns a snapshot of entries, newest first.</summary>
    public IReadOnlyList<Entry> GetAll()
    {
        lock (_lock)
        {
            return _entries.ToList();
        }
    }

    /// <summary>Removes the entry at <paramref name="index"/> (0 = newest).</summary>
    public bool RemoveAt(int index)
    {
        lock (_lock)
        {
            if (index < 0 || index >= _entries.Count) return false;
            var node = _entries.First!;
            for (int i = 0; i < index; i++) node = node.Next!;
            _entries.Remove(node);
            return true;
        }
    }

    /// <summary>Wipes the entire history.</summary>
    public void Clear()
    {
        lock (_lock) { _entries.Clear(); }
    }

    /// <summary>Test/UI helper.</summary>
    public int Count { get { lock (_lock) return _entries.Count; } }

    // ── Pure helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Heuristic detection of generated-password-shaped text. Length 12-64,
    /// no whitespace, contains at least 3 of: upper, lower, digit, symbol.
    /// Tunable per false-positive feedback; we'd rather over-tag (UI shows
    /// 🔒) than under-tag (silently keep a password in plaintext history).
    /// </summary>
    public static bool LooksLikePassword(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Length < 12 || text.Length > 64) return false;
        if (text.Any(char.IsWhiteSpace)) return false;

        bool hasUpper  = text.Any(char.IsUpper);
        bool hasLower  = text.Any(char.IsLower);
        bool hasDigit  = text.Any(char.IsDigit);
        bool hasSymbol = text.Any(c => !char.IsLetterOrDigit(c));

        int classes = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSymbol ? 1 : 0);
        return classes >= 3;
    }
}
