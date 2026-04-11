using System.Security.Cryptography;
using System.Text;

namespace VELO.Vault;

public static class VaultCrypto
{
    private const int Iterations = 310_000;
    private const int KeySize = 32; // AES-256
    private const int SaltSize = 32;
    private const int NonceSize = 12; // AES-GCM
    private const int TagSize = 16;

    public static byte[] GenerateSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    public static byte[] DeriveKey(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

    public static string HashPassword(string password, byte[] salt)
    {
        var hash = DeriveKey(password, salt);
        return Convert.ToBase64String(hash);
    }

    public static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Layout: [nonce(12)] [tag(16)] [ciphertext]
        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSize);
        ciphertext.CopyTo(result, NonceSize + TagSize);
        return result;
    }

    public static byte[] Decrypt(byte[] data, byte[] key)
    {
        var nonce      = data[..NonceSize];
        var tag        = data[NonceSize..(NonceSize + TagSize)];
        var ciphertext = data[(NonceSize + TagSize)..];
        var plaintext  = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    public static string EncryptString(string plaintext, byte[] key)
    {
        var encrypted = Encrypt(Encoding.UTF8.GetBytes(plaintext), key);
        return Convert.ToBase64String(encrypted);
    }

    public static string DecryptString(string ciphertext, byte[] key)
    {
        var decrypted = Decrypt(Convert.FromBase64String(ciphertext), key);
        return Encoding.UTF8.GetString(decrypted);
    }

    public static string GeneratePassword(int length = 24, bool uppercase = true, bool numbers = true, bool symbols = true)
    {
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string digits = "0123456789";
        const string special = "!@#$%^&*()-_=+[]{}|;:,.<>?";

        var chars = lower;
        if (uppercase) chars += upper;
        if (numbers)   chars += digits;
        if (symbols)   chars += special;

        return string.Create(length, chars, (span, pool) =>
        {
            var bytes = RandomNumberGenerator.GetBytes(length * 4);
            for (int i = 0; i < length; i++)
                span[i] = pool[(int)(BitConverter.ToUInt32(bytes, i * 4) % (uint)pool.Length)];
        });
    }
}
