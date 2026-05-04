using System.IO;
using System.Text.Json;
using SQLite;
using VELO.Import.Models;

namespace VELO.Import.Importers;

/// <summary>
/// Phase 3 / Sprint 4 — Reads bookmarks out of a <see cref="DetectedBrowser"/>
/// and yields a flat list of <see cref="ImportedBookmark"/>. Folder structure
/// is preserved as a path string ("Bookmarks Bar / Work / Vendors").
///
/// Chromium browsers store bookmarks as a JSON tree rooted at three named
/// roots (bookmark_bar, other, synced). Firefox uses places.sqlite —
/// moz_bookmarks (id/parent/title/fk) joined to moz_places (url).
/// </summary>
public sealed class BookmarksImporter
{
    public IReadOnlyList<ImportedBookmark> Import(DetectedBrowser browser)
    {
        var path = browser.IsChromium
            ? Path.Combine(browser.ProfilePath, "Bookmarks")
            : Path.Combine(browser.ProfilePath, "places.sqlite");
        if (!File.Exists(path)) return [];

        return browser.IsChromium ? ImportChromium(path) : ImportFirefox(path);
    }

    // ── Chromium (JSON tree) ────────────────────────────────────────────

    private static IReadOnlyList<ImportedBookmark> ImportChromium(string bookmarksPath)
    {
        var json = File.ReadAllText(bookmarksPath);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("roots", out var roots)) return [];

        var output = new List<ImportedBookmark>();
        foreach (var rootProp in roots.EnumerateObject())
        {
            // Skip the "sync_transaction_version" sibling that lives at the
            // same level (it isn't a bookmark tree).
            if (rootProp.Value.ValueKind != JsonValueKind.Object) continue;
            var rootName = TranslateRoot(rootProp.Name);
            WalkNode(rootProp.Value, rootName, output, isRoot: true);
        }
        return output;
    }

    private static void WalkNode(JsonElement node, string folderPath, List<ImportedBookmark> output, bool isRoot = false)
    {
        if (!node.TryGetProperty("type", out var typeEl)) return;
        var type = typeEl.GetString();

        if (type == "url")
        {
            var url   = node.TryGetProperty("url",  out var u) ? u.GetString() ?? "" : "";
            var name  = node.TryGetProperty("name", out var n) ? n.GetString() ?? url : url;
            if (string.IsNullOrEmpty(url)) return;
            output.Add(new ImportedBookmark(name, url, folderPath));
        }
        else if (type == "folder" && node.TryGetProperty("children", out var children))
        {
            // The three roots already have a translated label ("Bookmarks Bar",
            // "Other Bookmarks", "Mobile Bookmarks") supplied by the caller.
            // Don't re-stack the root's own "name" field — Chrome's "bookmark_bar"
            // root has name="Bookmarks bar", which would produce "Bookmarks Bar /
            // Bookmarks bar" duplication.
            var folderName = node.TryGetProperty("name", out var fn) ? fn.GetString() ?? "" : "";
            var sub = (isRoot || string.IsNullOrEmpty(folderName))
                        ? folderPath
                        : $"{folderPath} / {folderName}";
            foreach (var child in children.EnumerateArray())
                WalkNode(child, sub, output, isRoot: false);
        }
    }

    private static string TranslateRoot(string raw) => raw switch
    {
        "bookmark_bar" => "Bookmarks Bar",
        "other"        => "Other Bookmarks",
        "synced"       => "Mobile Bookmarks",
        _              => raw,
    };

    // ── Firefox (places.sqlite) ─────────────────────────────────────────

    private static IReadOnlyList<ImportedBookmark> ImportFirefox(string placesPath)
    {
        // We copy to a temp file because Firefox holds an SQLite WAL
        // lock when the browser is open. ReadOnly + ImmutableMode would
        // also work but copy is universal.
        var tempPath = Path.Combine(Path.GetTempPath(), $"velo-import-{Guid.NewGuid():N}.sqlite");
        try
        {
            File.Copy(placesPath, tempPath, overwrite: true);
            using var conn = new SQLiteConnection(tempPath, SQLiteOpenFlags.ReadOnly);

            // 1) Build folder-id → folder-name map by walking moz_bookmarks.type=2 (folder).
            var folders = conn.Query<MozBookmarkFolder>(
                "SELECT id, parent, COALESCE(title,'') AS title FROM moz_bookmarks WHERE type=2");
            var folderById = folders.ToDictionary(f => f.id);
            string FolderPath(int id)
            {
                if (id <= 0) return "";
                if (!folderById.TryGetValue(id, out var f)) return "";
                if (f.parent <= 0 || string.IsNullOrEmpty(f.title)) return f.title;
                var parent = FolderPath(f.parent);
                return string.IsNullOrEmpty(parent) ? f.title : $"{parent} / {f.title}";
            }

            // 2) Bookmarks: type=1 with non-null fk (foreign key to moz_places).
            var rows = conn.Query<MozBookmarkLink>(@"
                SELECT b.id, b.parent, COALESCE(b.title,'') AS title, p.url
                FROM moz_bookmarks b
                JOIN moz_places   p ON p.id = b.fk
                WHERE b.type = 1
                  AND p.url NOT LIKE 'place:%'");

            var output = new List<ImportedBookmark>(rows.Count);
            foreach (var r in rows)
            {
                var name = string.IsNullOrEmpty(r.title) ? r.url : r.title;
                output.Add(new ImportedBookmark(name, r.url, FolderPath(r.parent)));
            }
            return output;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    // sqlite-net-pcl row mappers (lowercase to match query column casing)
    private sealed class MozBookmarkFolder
    {
        public int id      { get; set; }
        public int parent  { get; set; }
        public string title { get; set; } = "";
    }

    private sealed class MozBookmarkLink
    {
        public int id      { get; set; }
        public int parent  { get; set; }
        public string title { get; set; } = "";
        public string url   { get; set; } = "";
    }
}

/// <summary>One bookmark row produced by the importer.</summary>
public sealed record ImportedBookmark(string Title, string Url, string FolderPath);
