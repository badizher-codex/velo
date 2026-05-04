namespace VELO.Import.Models;

public enum BrowserKind { Chrome, Edge, Brave, Vivaldi, Opera, Firefox }

/// <summary>
/// Phase 3 / Sprint 4 — A browser found on disk during import detection.
/// Profile path is used by importers to locate Bookmarks / History / passwords.
/// </summary>
public sealed record DetectedBrowser(
    BrowserKind Kind,
    string      DisplayName,
    string      ProfileName,
    string      ProfilePath)
{
    /// <summary>True for any Chromium-derived browser (Chrome, Edge, Brave…).</summary>
    public bool IsChromium => Kind != BrowserKind.Firefox;
}

/// <summary>What the user wants to import (per checkbox in the wizard).</summary>
public sealed class ImportOptions
{
    public bool Bookmarks       { get; set; } = true;
    public bool History         { get; set; } = true;
    public bool Passwords       { get; set; }
    public bool Cookies         { get; set; }
    public bool SearchEngines   { get; set; }
    /// <summary>Maximum number of history entries to import (newest first).</summary>
    public int  HistoryMaxItems { get; set; } = 1000;
    /// <summary>How many days back to scan history.</summary>
    public int  HistoryMaxDays  { get; set; } = 90;
}

/// <summary>Per-section result: imported / skipped / failed counts + any error.</summary>
public sealed class ImportResult
{
    public int  BookmarksImported   { get; set; }
    public int  HistoryImported     { get; set; }
    public int  PasswordsImported   { get; set; }
    public int  CookiesImported     { get; set; }
    public List<string> Warnings    { get; } = [];
    public List<string> Errors      { get; } = [];

    public int TotalImported =>
        BookmarksImported + HistoryImported + PasswordsImported + CookiesImported;
}
