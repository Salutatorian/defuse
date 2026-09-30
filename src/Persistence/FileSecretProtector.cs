using System.Security.Cryptography;
using System.Text;
using Defuse.Application;

namespace Defuse.Persistence;

public sealed class FileSecretProtector : ISecretProtector
{
    private readonly byte[] _key;

    public FileSecretProtector(string keyPath)
    {
        var directory = Path.GetDirectoryName(keyPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        if (!File.Exists(keyPath))
        {
            File.WriteAllBytes(keyPath, RandomNumberGenerator.GetBytes(32));
            TryRestrict(keyPath);
        }
        var stored = File.ReadAllBytes(keyPath);
        if (stored.Length != 32)
            throw new InvalidOperationException("The local key file is unreadable.");
        _key = stored;
    }

    private static void TryRestrict(string keyPath)
    {
        try
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (PlatformNotSupportedException)
        {
        }
    }

    public byte[] Protect(string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(_key, 16))
            aes.Encrypt(nonce, plain, cipher, tag);
        CryptographicOperations.ZeroMemory(plain);
        var result = new byte[nonce.Length + tag.Length + cipher.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, nonce.Length);
        cipher.CopyTo(result, nonce.Length + tag.Length);
        return result;
    }

    public string Unprotect(byte[] payload)
    {
        if (payload.Length < 28)
            throw new CryptographicException("The secret is too short.");
        var nonce = payload.AsSpan(0, 12);
        var tag = payload.AsSpan(12, 16);
        var cipher = payload.AsSpan(28);
        var plain = new byte[cipher.Length];
        using (var aes = new AesGcm(_key, 16))
            aes.Decrypt(nonce, cipher, tag, plain);
        try
        {
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
