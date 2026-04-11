using Microsoft.Extensions.Logging;
using VELO.Data.Models;
using VELO.Data.Repositories;

namespace VELO.Vault;

public class VaultService(
    PasswordRepository passwordRepo,
    SettingsRepository settings,
    ILogger<VaultService> logger)
{
    private readonly PasswordRepository _passwordRepo = passwordRepo;
    private readonly SettingsRepository _settings = settings;
    private readonly ILogger<VaultService> _logger = logger;

    private byte[]? _encryptionKey;
    private DateTime _lastActivity = DateTime.MinValue;

    public bool IsUnlocked => _encryptionKey != null;

    public async Task<bool> UnlockAsync(string masterPassword)
    {
        var saltBase64 = await _settings.GetAsync(SettingKeys.VaultSalt);
        var hashStored = await _settings.GetAsync(SettingKeys.VaultMasterPasswordHash);

        if (saltBase64 == null || hashStored == null)
        {
            _logger.LogError("Vault not initialized — no salt or hash found");
            return false;
        }

        var salt = Convert.FromBase64String(saltBase64);
        var hashAttempt = VaultCrypto.HashPassword(masterPassword, salt);

        if (hashAttempt != hashStored)
        {
            _logger.LogWarning("Vault unlock failed — wrong master password");
            return false;
        }

        _encryptionKey = VaultCrypto.DeriveKey(masterPassword, salt);
        _lastActivity = DateTime.UtcNow;
        _logger.LogInformation("Vault unlocked");
        return true;
    }

    public async Task InitializeAsync(string masterPassword)
    {
        var salt = VaultCrypto.GenerateSalt();
        var hash = VaultCrypto.HashPassword(masterPassword, salt);

        await _settings.SetAsync(SettingKeys.VaultSalt, Convert.ToBase64String(salt));
        await _settings.SetAsync(SettingKeys.VaultMasterPasswordHash, hash);

        _encryptionKey = VaultCrypto.DeriveKey(masterPassword, salt);
        _lastActivity = DateTime.UtcNow;
        _logger.LogInformation("Vault initialized");
    }

    public bool IsInitialized(string? saltBase64) => saltBase64 != null;

    public void Lock()
    {
        if (_encryptionKey != null)
            Array.Clear(_encryptionKey, 0, _encryptionKey.Length);
        _encryptionKey = null;
        _logger.LogInformation("Vault locked");
    }

    public bool CheckAutoLock(int autoLockMinutes)
    {
        if (_encryptionKey == null) return false;
        if (DateTime.UtcNow - _lastActivity > TimeSpan.FromMinutes(autoLockMinutes))
        {
            Lock();
            return true; // was locked
        }
        return false;
    }

    public void RecordActivity() => _lastActivity = DateTime.UtcNow;

    public async Task<List<PasswordEntry>> GetAllAsync()
    {
        EnsureUnlocked();
        var entries = await _passwordRepo.GetAllAsync();
        foreach (var e in entries)
        {
            e.Password = VaultCrypto.DecryptString(e.Password, _encryptionKey!);
            if (e.Notes != null)
                e.Notes = VaultCrypto.DecryptString(e.Notes, _encryptionKey!);
        }
        return entries;
    }

    public async Task SaveAsync(PasswordEntry entry)
    {
        EnsureUnlocked();
        var toSave = new PasswordEntry
        {
            Id         = entry.Id,
            SiteName   = entry.SiteName,
            Url        = entry.Url,
            Username   = entry.Username,
            Password   = VaultCrypto.EncryptString(entry.Password, _encryptionKey!),
            Notes      = entry.Notes != null ? VaultCrypto.EncryptString(entry.Notes, _encryptionKey!) : null,
            ContainerId = entry.ContainerId,
            CreatedAt  = entry.CreatedAt,
            ModifiedAt = DateTime.UtcNow
        };
        await _passwordRepo.SaveAsync(toSave);
    }

    public async Task DeleteAsync(string id)
    {
        EnsureUnlocked();
        await _passwordRepo.DeleteAsync(id);
    }

    public async Task<PasswordEntry?> FindForUrlAsync(string url)
    {
        EnsureUnlocked();
        var entry = await _passwordRepo.FindByUrlAsync(url);
        if (entry == null) return null;

        entry.Password = VaultCrypto.DecryptString(entry.Password, _encryptionKey!);
        if (entry.Notes != null)
            entry.Notes = VaultCrypto.DecryptString(entry.Notes, _encryptionKey!);
        return entry;
    }

    public static string GeneratePassword(int length = 24, bool uppercase = true, bool numbers = true, bool symbols = true)
        => VaultCrypto.GeneratePassword(length, uppercase, numbers, symbols);

    private void EnsureUnlocked()
    {
        if (_encryptionKey == null)
            throw new InvalidOperationException("Vault is locked. Unlock with master password first.");
    }
}
