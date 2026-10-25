using System.Security.Cryptography;
using System.Text;

namespace PegasusDeck.Core.Security;

/// <summary>Tokens de dispositivo: 256 bits aleatorios; en disco solo se guarda su hash.</summary>
public static class Tokens
{
    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string NewDeviceId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    public static string NewOperationId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    public static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    /// <summary>Compara un secreto con un hash guardado en tiempo constante.</summary>
    public static bool Matches(string secret, string storedHash)
    {
        var a = Encoding.ASCII.GetBytes(Hash(secret));
        var b = Encoding.ASCII.GetBytes(storedHash);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
