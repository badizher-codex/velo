using VELO.Core.Localization;
using VELO.Security.Threats;
using Xunit;

namespace VELO.Security.Tests;

// v2.4.63 — The threats panel showed only "blocked" plus three buttons; the
// reason lived behind "Explain", which calls the local model, so with no model
// running the user never got one. WhyBlocked is the always-available answer.
//
// Shares a collection with BlockExplanationServiceTests because both read the
// LocalizationService singleton and these tests switch its language.
[Collection("Localization")]
public class BlockEntryWhyBlockedTests
{
    private static BlockEntry Entry(BlockKind kind, BlockSource source = BlockSource.RequestGuard)
        => new() { Host = "tracker.example.com", FullUrl = "https://tracker.example.com/t.js", Kind = kind, Source = source };

    [Theory]
    [InlineData(BlockKind.Tracker)]
    [InlineData(BlockKind.Malware)]
    [InlineData(BlockKind.Ads)]
    [InlineData(BlockKind.Fingerprint)]
    [InlineData(BlockKind.Script)]
    [InlineData(BlockKind.Social)]
    [InlineData(BlockKind.Other)]
    public void WhyBlocked_IsPopulatedForEveryKind(BlockKind kind)
    {
        var text = Entry(kind).WhyBlocked;

        Assert.False(string.IsNullOrWhiteSpace(text));
        // A missing key makes T() return the key itself — that must never reach the UI.
        Assert.DoesNotContain("threatspanel.", text);
    }

    [Theory]
    [InlineData(BlockSource.GoldenList)]
    [InlineData(BlockSource.Malwaredex)]
    [InlineData(BlockSource.AIEngine)]
    [InlineData(BlockSource.UserRule)]
    [InlineData(BlockSource.StaticList)]
    [InlineData(BlockSource.RequestGuard)]
    [InlineData(BlockSource.DownloadGuard)]
    public void WhyBlocked_NamesEverySource(BlockSource source)
    {
        var text = Entry(BlockKind.Tracker, source).WhyBlocked;

        Assert.DoesNotContain("threatspanel.", text);
        Assert.Contains(LocalizationService.Current.T(BlockEntry.SourceKey(source)), text);
    }

    // All eight languages must carry the new strings — a gap would silently fall
    // back to Spanish for that user.
    [Fact]
    public void WhyBlocked_IsTranslatedInEveryLanguage()
    {
        var original = LocalizationService.Current.Language;
        try
        {
            var seen = new List<string>();
            foreach (var lang in LocalizationService.Languages.Keys)
            {
                LocalizationService.Current.SetLanguage(lang);
                var text = Entry(BlockKind.Fingerprint).WhyBlocked;

                Assert.DoesNotContain("threatspanel.", text);
                seen.Add(text);
            }

            // Eight distinct translations, not the same string echoed back.
            Assert.Equal(LocalizationService.Languages.Count, seen.Distinct().Count());
        }
        finally
        {
            LocalizationService.Current.SetLanguage(original);
        }
    }

    [Fact]
    public void StaticTemplate_FollowsTheUiLanguage_AndNamesTheHost()
    {
        var original = LocalizationService.Current.Language;
        try
        {
            var entry = Entry(BlockKind.Malware);

            LocalizationService.Current.SetLanguage("es");
            var es = BlockExplanationService.LookupStaticTemplate(entry);
            LocalizationService.Current.SetLanguage("en");
            var en = BlockExplanationService.LookupStaticTemplate(entry);

            Assert.Contains(entry.Host, es);
            Assert.Contains(entry.Host, en);
            Assert.NotEqual(es, en);
        }
        finally
        {
            LocalizationService.Current.SetLanguage(original);
        }
    }
}
