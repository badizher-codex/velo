using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SQLite;
using VELO.Import.Models;

namespace VELO.Import.Importers;

/// <summary>
/// Phase 3 / Sprint 4 — Decrypts saved passwords from a Chromium-derived
/// browser (Chrome / Edge / Brave / Vivaldi / Opera) using Windows DPAPI v10:
///   1. Open <c>Local State</c> JSON, extract <c>os_crypt.encrypted_key</c>.
///   2. Base64-decode, strip the 5-byte "DPAPI" prefix.
///   3. Unprotect with DPAPI under the current user → 32-byte AES key.
///   4. For each row in <c>Login Data</c>.<c>logins</c>, the
///      <c>password_value</c> blob is "v10" + 12-byte IV + ciphertext + 16-byte tag.
///   5. AES-GCM decrypt with the unprotected key.
///
/// Firefox is NOT supported in this sprint — NSS (libnss3.dll) P/Invoke is
/// a separate piece of work; <see cref="ImportFirefox"/> returns a marker
/// warning that pipes back through the wizard.
/// </summary>
public sealed class PasswordImporter
{
    /// <summary>Public so the orchestrator can flag Firefox as unsupported.</summary>
    public const string FirefoxNotSupportedMessage =
        "Firefox password import requires the NSS library (libnss3.dll) and is not yet supported. " +
        "Use Firefox's built-in 'Export Logins' (CSV) and import via Settings → Vault for now.";

    public PasswordImportResult Import(DetectedBrowser browser)
    {
        if (browser.Kind == BrowserKind.Firefox)
            return new PasswordImportResult([], [FirefoxNotSupportedMessage]);

        return ImportChromium(browser);
    }

    private static PasswordImportResult ImportChromium(DetectedBrowser browser)
    {
        var warnings = new List<string>();
        var creds    = new List<ImportedPassword>();

        // ── 1. Read Local State for the encrypted key ─────────────────
        // Local State sits in the User Data folder (one level up from the
        // profile). We tolerate either layout (Default + User Data parent
        // is standard; Opera flattens but Local State is alongside).
        var userDataDir = Directory.GetParent(browser.ProfilePath)?.FullName ?? browser.ProfilePath;
        var localStatePath = File.Exists(Path.Combine(userDataDir, "Local State"))
            ? Path.Combine(userDataDir, "Local State")
            : Path.Combine(browser.ProfilePath, "Local State");

        if (!File.Exists(localStatePath))
        {
            warnings.Add($"Local State file not found near '{browser.ProfilePath}'. Cannot derive AES key.");
            return new PasswordImportResult(creds, warnings);
        }

        byte[] aesKey;
        try
        {
            aesKey = ExtractMasterKey(File.ReadAllText(localStatePath));
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to derive AES key from Local State: {ex.Message}");
            return new PasswordImportResult(creds, warnings);
        }

        // ── 2. Open Login Data SQLite (copy because the source may be locked) ─
        var loginDataPath = Path.Combine(browser.ProfilePath, "Login Data");
        if (!File.Exists(loginDataPath))
        {
            warnings.Add("'Login Data' file not found in profile.");
            return new PasswordImportResult(creds, warnings);
        }
        var tempPath = Path.Combine(Path.GetTempPath(), $"velo-import-pwd-{Guid.NewGuid():N}.sqlite");
        try
        {
            File.Copy(loginDataPath, tempPath, overwrite: true);
            using var conn = new SQLiteConnection(tempPath, SQLiteOpenFlags.ReadOnly);

            var rows = conn.Query<LoginRow>(@"
                SELECT origin_url, username_value, password_value
                FROM logins
                WHERE blacklisted_by_user = 0
                  AND length(password_value) > 0");

            int decryptFailures = 0;
            foreach (var r in rows)
            {
                if (r.password_value == null || r.password_value.Length == 0) continue;
                try
                {
                    var plain = DecryptChromiumBlob(r.password_value, aesKey);
                    if (string.IsNullOrEmpty(plain)) continue;
                    creds.Add(new ImportedPassword(
                        Url:      r.origin_url ?? "",
                        Username: r.username_value ?? "",
                        Password: plain));
                }
                catch
                {
                    decryptFailures++;
                }
            }

            if (decryptFailures > 0)
                warnings.Add($"{decryptFailures} password(s) couldn't be decrypted. " +
                             "Most common cause: the profile is from a different Windows user.");
        }
        catch (Exception ex)
        {
            warnings.Add($"Login Data read failed: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }

        return new PasswordImportResult(creds, warnings);
    }

    // ── Pure helpers (unit-testable without DPAPI) ───────────────────

    /// <summary>
    /// Extracts the encrypted_key bytes from a <c>Local State</c> JSON
    /// document and feeds them to DPAPI for decryption. Public so tests
    /// can verify base64-decode + "DPAPI" prefix stripping in isolation.
    /// </summary>
    public static byte[] ExtractMasterKey(string localStateJson)
    {
        using var doc = JsonDocument.Parse(localStateJson);
        if (!doc.RootElement.TryGetProperty("os_crypt", out var os) ||
            !os.TryGetProperty("encrypted_key", out var keyEl) ||
            keyEl.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("os_crypt.encrypted_key missing");
        }

        var encrypted = Convert.FromBase64String(keyEl.GetString() ?? "");
        // The blob starts with the literal 5-byte tag "DPAPI"; strip it
        // before handing to ProtectedData.
        if (encrypted.Length < 6 || Encoding.ASCII.GetString(encrypted, 0, 5) != "DPAPI")
            throw new InvalidOperationException("encrypted_key is not in expected DPAPI format");

        var withoutPrefix = new byte[encrypted.Length - 5];
        Buffer.BlockCopy(encrypted, 5, withoutPrefix, 0, withoutPrefix.Length);

#pragma warning disable CA1416 // ProtectedData is Windows-only — VELO is too.
        return ProtectedData.Unprotect(withoutPrefix, optionalEntropy: null,
                                       scope: DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
    }

    /// <summary>
    /// Decrypts one <c>password_value</c> blob using the unprotected master key.
    /// Format: 3-byte "v10" prefix + 12-byte IV + ciphertext + 16-byte GCM tag.
    /// </summary>
    public static string DecryptChromiumBlob(byte[] blob, byte[] aesKey)
    {
        if (blob == null || blob.Length < 3 + 12 + 16)
            throw new ArgumentException("blob too short", nameof(blob));
        var prefix = Encoding.ASCII.GetString(blob, 0, 3);
        if (prefix != "v10" && prefix != "v11")
            throw new InvalidOperationException($"unsupported blob prefix '{prefix}'");

        const int IvLen = 12;
        const int TagLen = 16;
        var iv         = new byte[IvLen];
        var tag        = new byte[TagLen];
        var cipher     = new byte[blob.Length - 3 - IvLen - TagLen];
        var plaintext  = new byte[cipher.Length];

        Buffer.BlockCopy(blob, 3,                     iv,     0, IvLen);
        Buffer.BlockCopy(blob, 3 + IvLen,             cipher, 0, cipher.Length);
        Buffer.BlockCopy(blob, blob.Length - TagLen,  tag,    0, TagLen);

        using var aes = new AesGcm(aesKey, TagLen);
        aes.Decrypt(iv, cipher, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    // sqlite-net-pcl row mapper
    private sealed class LoginRow
    {
        public string? origin_url     { get; set; }
        public string? username_value { get; set; }
        public byte[]? password_value { get; set; }
    }
}

/// <summary>Outcome of <see cref="PasswordImporter.Import"/>.</summary>
public sealed record PasswordImportResult(
    IReadOnlyList<ImportedPassword> Credentials,
    IReadOnlyList<string>           Warnings);

/// <summary>One credential row produced by the password importer.</summary>
public sealed record ImportedPassword(string Url, string Username, string Password);
