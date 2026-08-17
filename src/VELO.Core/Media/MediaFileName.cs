namespace VELO.Core.Media;

/// <summary>
/// Turns "what is playing" into a filename Windows will actually accept.
///
/// A page title is arbitrary text written by someone who was not thinking
/// about filesystems: it carries slashes, colons, quotes, emoji and, on a bad
/// day, four hundred characters. Handing that straight to a save dialog fails
/// in ways that are hard to read — Windows does not reject a trailing dot, it
/// silently strips it, so the file lands next to where you asked for it.
///
/// Pure and static on purpose. The interesting part of this feature is a set
/// of rules about a hostile string, and rules stuck inside a dialog handler
/// cannot be tested (lesson #55).
/// </summary>
public static class MediaFileName
{
    /// <summary>
    /// Characters Windows rejects in a filename, plus the control range.
    /// <c>:</c> and <c>?</c> matter most here: "Artist: Song" and titles that
    /// end in a question mark are entirely ordinary.
    /// </summary>
    private static readonly char[] Invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>
    /// MS-DOS device names, still reserved. The extension does not save you —
    /// <c>CON.webm</c> is as reserved as <c>CON</c> — so this is checked
    /// against the base name and the file gets an underscore in front.
    /// </summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Cap on the base name. Well under MAX_PATH once a Downloads path and an
    /// extension are added, and long enough for any real track title.
    /// </summary>
    public const int MaxBaseLength = 120;

    /// <summary>
    /// Builds a suggested filename from a media title.
    /// </summary>
    /// <param name="title">
    /// What the page says is playing. Null, empty or unusable falls back.
    /// </param>
    /// <param name="extension">Including the dot, e.g. <c>.webm</c>.</param>
    /// <param name="fallbackBase">
    /// Used when the title yields nothing — the caller's existing behaviour,
    /// so a page without a title is no worse off than before.
    /// </param>
    public static string Suggest(string? title, string extension, string fallbackBase)
    {
        var name = Sanitize(title);
        if (name.Length == 0) name = Sanitize(fallbackBase);
        if (name.Length == 0) name = "media";

        // Checked after truncation as well as before: cutting a long title at
        // 120 characters can leave trailing dots or spaces that were harmless
        // in the middle of the string.
        return name + extension;
    }

    /// <summary>
    /// The rules, in an order that matters: strip, collapse, trim, cap, trim
    /// again, then de-reserve.
    /// </summary>
    public static string Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var chars = new List<char>(raw.Length);
        foreach (var c in raw)
        {
            // Control characters are invalid in a filename and invisible in a
            // title, which is the worst combination to debug.
            if (char.IsControl(c)) { chars.Add(' '); continue; }
            chars.Add(Array.IndexOf(Invalid, c) >= 0 ? ' ' : c);
        }

        // Collapsing runs of whitespace keeps "A / B" from becoming "A  B".
        var collapsed = new System.Text.StringBuilder(chars.Count);
        var lastWasSpace = false;
        foreach (var c in chars)
        {
            var isSpace = char.IsWhiteSpace(c);
            if (isSpace && lastWasSpace) continue;
            collapsed.Append(isSpace ? ' ' : c);
            lastWasSpace = isSpace;
        }

        var name = collapsed.ToString().Trim();

        if (name.Length > MaxBaseLength) name = name[..MaxBaseLength];

        // Windows strips trailing dots and spaces without telling anyone, so
        // "Episode 1..." would be saved as "Episode 1" — the file is then not
        // where the caller thinks it is. Trim them ourselves so the name we
        // hand out is the name that ends up on disk.
        name = name.TrimEnd('.', ' ');

        if (Reserved.Contains(name)) name = "_" + name;

        return name;
    }
}
