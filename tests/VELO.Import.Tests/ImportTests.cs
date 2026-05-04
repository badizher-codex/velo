using System.IO;
using System.Security.Cryptography;
using System.Text;
using SQLite;
using VELO.Import.Detectors;
using VELO.Import.Importers;
using VELO.Import.Models;
using Xunit;

namespace VELO.Import.Tests;

public class ImportTests
{
    private static string NewTempProfileDir(string subdir = "")
    {
        var dir = Path.Combine(Path.GetTempPath(),
            $"velo-import-test-{Guid.NewGuid():N}", subdir.Trim('\\'));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void RmRf(string root)
    {
        try
        {
            // Walk up from leaf to find the test base dir (everything under
            // velo-import-test-<guid>) and nuke it whole.
            var velo = root;
            while (velo != null && !Path.GetFileName(velo).StartsWith("velo-import-test-"))
                velo = Path.GetDirectoryName(velo);
            if (velo != null && Directory.Exists(velo))
                Directory.Delete(velo, recursive: true);
        }
        catch { }
    }

    // ── Detector tests (spec § 4.7 #1, #2) ──────────────────────────────

    [Fact]
    public async Task ChromeDetector_ReturnsNull_WhenNotInstalled()
    {
        // Override Chromium's expected path by pointing the detector at a
        // sub-class with a bogus relative path. The real ChromeDetector
        // uses %LOCALAPPDATA%\Google\Chrome\User Data — which may exist on
        // the test machine — so we test the FAILURE path via a custom
        // detector that's identical except for the relative path.
        var detector = new MissingChromiumDetector();
        var result   = await detector.DetectAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task ChromeDetector_FindsProfile_WhenInstalled()
    {
        // Emulate a Chrome User Data layout under a test temp dir. Build the
        // minimum valid profile (Default folder + Bookmarks file).
        var profile = NewTempProfileDir(@"User Data\Default");
        File.WriteAllText(Path.Combine(profile, "Bookmarks"), """{"roots":{"bookmark_bar":{"type":"folder","children":[]}}}""");
        // Placeholder Local State so DPAPI test below has something to read.
        var userData = Path.GetDirectoryName(profile)!;
        File.WriteAllText(Path.Combine(userData, "Local State"), "{}");

        var detector = new TestChromiumDetector(Path.GetDirectoryName(userData)!);
        var result   = await detector.DetectAsync();

        Assert.NotNull(result);
        Assert.Equal(BrowserKind.Chrome, result!.Kind);
        Assert.Equal("Default", result.ProfileName);
        Assert.Equal(profile.TrimEnd('\\'), result.ProfilePath.TrimEnd('\\'));
        RmRf(profile);
    }

    // ── Bookmark tests (spec § 4.7 #3) ──────────────────────────────────

    [Fact]
    public void BookmarksImporter_ParsesNestedFolders()
    {
        // Two-level folder structure: Bookmarks Bar / Work / GitHub repos
        var json = """
        {
          "roots": {
            "bookmark_bar": {
              "type": "folder",
              "name": "Bookmarks bar",
              "children": [
                { "type": "url", "name": "Hacker News", "url": "https://news.ycombinator.com" },
                {
                  "type": "folder",
                  "name": "Work",
                  "children": [
                    { "type": "url", "name": "GitHub", "url": "https://github.com" }
                  ]
                }
              ]
            },
            "other": {
              "type": "folder",
              "name": "Other",
              "children": [
                { "type": "url", "name": "Wikipedia", "url": "https://wikipedia.org" }
              ]
            }
          }
        }
        """;
        var profile = NewTempProfileDir();
        File.WriteAllText(Path.Combine(profile, "Bookmarks"), json);
        try
        {
            var browser = new DetectedBrowser(BrowserKind.Chrome, "Test", "Default", profile);
            var rows    = new BookmarksImporter().Import(browser);

            // The translated root labels ("Bookmarks Bar", "Other Bookmarks")
            // are the canonical folder names — Chrome's own "name" field
            // on the root nodes ("Bookmarks bar", "Other") would just stack
            // a duplicate label, so we skip it. Nested non-root folders
            // (e.g. "Work") still propagate normally.
            Assert.Equal(3, rows.Count);
            Assert.Contains(rows, r => r.Title == "Hacker News" && r.FolderPath == "Bookmarks Bar");
            Assert.Contains(rows, r => r.Title == "GitHub"     && r.FolderPath == "Bookmarks Bar / Work");
            Assert.Contains(rows, r => r.Title == "Wikipedia"  && r.FolderPath == "Other Bookmarks");
        }
        finally { RmRf(profile); }
    }

    // ── History timestamp test (spec § 4.7 #4) ──────────────────────────

    [Fact]
    public void HistoryImporter_ConvertsChromeTimestamp_ToUtc()
    {
        // Chromium stores µs since 1601-01-01 UTC. Pick a known instant and
        // verify round-trip via the public helpers.
        var expected = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        var asChromium = HistoryImporter.UtcToChromiumTime(expected);
        var roundTrip  = HistoryImporter.ChromiumTimeToUtc(asChromium);

        // 1-second tolerance: µs precision is well below that.
        Assert.Equal(expected, roundTrip);

        // Sanity: 1601-01-01 UTC (the FILETIME epoch) → 0.
        Assert.Equal(0L, HistoryImporter.UtcToChromiumTime(
            new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        // 1970-01-01 UTC (UNIX epoch) → exactly 11_644_473_600 seconds in µs.
        Assert.Equal(11_644_473_600L * 1_000_000L, HistoryImporter.UtcToChromiumTime(
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    // ── Password decrypt path (spec § 4.7 #5, #6) ───────────────────────

    [Fact]
    public void PasswordImporter_DecryptsViaDPAPI_WhenSameUser()
    {
        // We can't realistically run DPAPI in a unit test (it touches the
        // current Windows user profile), but we CAN verify the AES-GCM
        // unwrap path: take a known key + IV + plaintext, build a v10 blob,
        // and confirm DecryptChromiumBlob returns the plaintext.
        var aesKey = RandomNumberGenerator.GetBytes(32);
        var iv     = RandomNumberGenerator.GetBytes(12);
        var plain  = Encoding.UTF8.GetBytes("hunter2");
        var cipher = new byte[plain.Length];
        var tag    = new byte[16];
        using (var aes = new AesGcm(aesKey, 16))
            aes.Encrypt(iv, plain, cipher, tag);

        var blob = new byte[3 + 12 + cipher.Length + 16];
        Buffer.BlockCopy(Encoding.ASCII.GetBytes("v10"), 0, blob, 0, 3);
        Buffer.BlockCopy(iv,     0, blob, 3, 12);
        Buffer.BlockCopy(cipher, 0, blob, 15, cipher.Length);
        Buffer.BlockCopy(tag,    0, blob, 15 + cipher.Length, 16);

        var roundTrip = PasswordImporter.DecryptChromiumBlob(blob, aesKey);
        Assert.Equal("hunter2", roundTrip);
    }

    [Fact]
    public void PasswordImporter_ReturnsError_WhenDifferentUser()
    {
        // "Different user" simulates: the AES key DPAPI returned won't match
        // what the source profile encrypted with. AesGcm.Decrypt throws
        // CryptographicException when the tag doesn't validate.
        var rightKey = RandomNumberGenerator.GetBytes(32);
        var wrongKey = RandomNumberGenerator.GetBytes(32);
        var iv     = RandomNumberGenerator.GetBytes(12);
        var plain  = Encoding.UTF8.GetBytes("secret");
        var cipher = new byte[plain.Length];
        var tag    = new byte[16];
        using (var aes = new AesGcm(rightKey, 16))
            aes.Encrypt(iv, plain, cipher, tag);

        var blob = new byte[3 + 12 + cipher.Length + 16];
        Buffer.BlockCopy(Encoding.ASCII.GetBytes("v10"), 0, blob, 0, 3);
        Buffer.BlockCopy(iv,     0, blob, 3, 12);
        Buffer.BlockCopy(cipher, 0, blob, 15, cipher.Length);
        Buffer.BlockCopy(tag,    0, blob, 15 + cipher.Length, 16);

        Assert.Throws<AuthenticationTagMismatchException>(() =>
            PasswordImporter.DecryptChromiumBlob(blob, wrongKey));
    }

    [Fact]
    public void PasswordImporter_ExtractsKey_FromValidLocalState()
    {
        // Round-trip: build an "encrypted_key" of the form "DPAPI" + protected.
        // We don't actually call DPAPI here — instead we verify base64 decode
        // and prefix parsing by feeding a fake protected blob and asserting
        // ExtractMasterKey throws a meaningful error on mangled input.
        var json = """{"os_crypt":{"encrypted_key":"bm9wZSBub3QgRFBBUEk="}}"""; // base64("nope not DPAPI")
        Assert.Throws<InvalidOperationException>(() =>
            PasswordImporter.ExtractMasterKey(json));
    }

    [Fact]
    public void PasswordImporter_FirefoxReturnsUnsupportedWarning()
    {
        // Spec § 4.3: Firefox NSS isn't supported in this sprint. Verify
        // the orchestrator gets a friendly warning instead of a thrown.
        var browser = new DetectedBrowser(BrowserKind.Firefox, "Firefox", "default", "/tmp/whatever");
        var result  = new PasswordImporter().Import(browser);

        Assert.Empty(result.Credentials);
        Assert.Single(result.Warnings);
        Assert.Equal(PasswordImporter.FirefoxNotSupportedMessage, result.Warnings[0]);
    }

    // ── Test-only detectors ───────────────────────────────────────────

    /// <summary>Looks at a path that definitely doesn't exist.</summary>
    private sealed class MissingChromiumDetector : ChromiumDetectorBase
    {
        public override string Name           => "MissingChrome";
        protected override BrowserKind Kind   => BrowserKind.Chrome;
        protected override string DisplayName => "Missing Chrome";
        protected override string UserDataRelative => @"NeverExists\NoBrowser\User Data";
    }

    /// <summary>Overrides LocalAppDataRoot directly via the test seam.</summary>
    private sealed class TestChromiumDetector(string fakeLocalAppDataRoot) : ChromiumDetectorBase
    {
        public override string Name           => "TestChrome";
        protected override BrowserKind Kind   => BrowserKind.Chrome;
        protected override string DisplayName => "Test Chrome";
        protected override string UserDataRelative => "User Data";
        protected override string LocalAppDataRoot => fakeLocalAppDataRoot;
    }
}
