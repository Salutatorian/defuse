using System.Security.Cryptography;
using System.Text;
using Defuse.Application;

namespace Defuse.Persistence;

public sealed class DpapiSecretProtector : ISecretProtector
{
    public byte[] Protect(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string Unprotect(byte[] payload)
    {
        var bytes = ProtectedData.Unprotect(payload, optionalEntropy: null, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
