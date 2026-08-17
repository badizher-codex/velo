using VELO.Core.Media;
using Xunit;

namespace VELO.Core.Tests;

/// <summary>
/// Phase 6 — naming the downloaded file after what is playing.
///
/// A page title is arbitrary text written by someone not thinking about
/// filesystems. Every case here is a real shape of title, not an invented
/// edge: colons and slashes are ordinary punctuation, question marks end
/// sentences, and titles routinely run past any sane filename length.
/// </summary>
public class MediaFileNameTests
{
    [Fact]
    public void A_normal_title_survives_intact()
    {
        // The whole point: this is what the maintainer was typing by hand.
        Assert.Equal("Benson Boone - Beautiful Things.webm",
            MediaFileName.Suggest("Benson Boone - Beautiful Things", ".webm", "audio"));
    }

    [Theory]
    [InlineData("AC/DC - Back in Black",     "AC DC - Back in Black")]
    [InlineData("Alien: Romulus",            "Alien Romulus")]
    [InlineData("What Is Love?",             "What Is Love")]
    [InlineData("He said \"hello\"",         "He said hello")]
    [InlineData("Rock * Roll | Live",        "Rock Roll Live")]
    [InlineData("a<b>c",                     "a b c")]
    public void Characters_windows_rejects_are_replaced_not_kept(string title, string expected)
    {
        Assert.Equal(expected, MediaFileName.Sanitize(title));
    }

    [Fact]
    public void Removing_a_character_does_not_leave_a_double_space()
    {
        // "AC/DC" becoming "AC  DC" would be a visible tell that something
        // mangled the name. Runs of whitespace collapse to one.
        Assert.DoesNotContain("  ", MediaFileName.Sanitize("AC / DC — Live // Tour"));
    }

    [Fact]
    public void Trailing_dots_and_spaces_are_trimmed()
    {
        // Windows strips these silently, so a caller that keeps them ends up
        // with the file somewhere other than the path it asked for. Trimming
        // here means the name we hand out is the name that lands on disk.
        Assert.Equal("Episode 1", MediaFileName.Sanitize("Episode 1..."));
        Assert.Equal("Episode 1", MediaFileName.Sanitize("Episode 1   "));
        Assert.Equal("Episode 1", MediaFileName.Sanitize("Episode 1 . . "));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void Reserved_device_names_get_out_of_the_way(string reserved)
    {
        // An extension does not lift the reservation — CON.webm is as
        // unusable as CON — so the base name itself has to change.
        Assert.Equal("_" + reserved, MediaFileName.Sanitize(reserved));
    }

    [Fact]
    public void A_long_title_is_capped_and_still_ends_cleanly()
    {
        var name = MediaFileName.Sanitize(new string('a', 400) + "...");

        Assert.Equal(MediaFileName.MaxBaseLength, name.Length);
        Assert.DoesNotContain(".", name);
    }

    [Fact]
    public void Truncation_cannot_leave_a_trailing_space()
    {
        // Cut at exactly the cap, the last character can be a space that was
        // perfectly fine in the middle of the string.
        var title = new string('a', MediaFileName.MaxBaseLength - 1) + " tail";
        var name  = MediaFileName.Sanitize(title);

        Assert.False(name.EndsWith(' '), $"'{name}' ends with a space");
        Assert.False(name.EndsWith('.'), $"'{name}' ends with a dot");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    [InlineData("...")]
    public void An_unusable_title_falls_back_instead_of_producing_junk(string? title)
    {
        // A page with no usable title must leave the user exactly where they
        // were before this feature existed, never with a file called ".webm".
        Assert.Equal("audio.webm", MediaFileName.Suggest(title, ".webm", "audio"));
    }

    [Fact]
    public void An_unusable_title_and_an_unusable_fallback_still_name_the_file()
    {
        Assert.Equal("media.ts", MediaFileName.Suggest("", ".ts", "///"));
    }

    [Fact]
    public void Emoji_and_accents_are_left_alone()
    {
        // NTFS takes these happily. Stripping them would mangle titles in most
        // of the languages VELO ships in, to solve a problem that is not real.
        Assert.Equal("Canción de cuna 🎵",
            MediaFileName.Sanitize("Canción de cuna 🎵"));
    }
}
