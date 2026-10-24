using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BotWatch.Persistence;

// AES-GCM: nonce | tag | ciphertext. ключ 32 байта из Encryption:Key (base64)
public sealed class SecretProtector
{
    public const string KeyConfigPath = "Encryption:Key";

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public SecretProtector(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            throw new InvalidOperationException($"{KeyConfigPath} is not configured. Generate one: openssl rand -base64 32");
        }

        _key = Convert.FromBase64String(base64Key);
        if (_key.Length != 32)
        {
            throw new InvalidOperationException($"{KeyConfigPath} must be 32 bytes");
        }
    }

    public byte[] Protect(byte[] plaintext)
    {
        var result = new byte[NonceSize + TagSize + plaintext.Length];
        var nonce = result.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, result.AsSpan(NonceSize + TagSize), result.AsSpan(NonceSize, TagSize));

        return result;
    }

    public byte[] Unprotect(byte[] data)
    {
        var plaintext = new byte[data.Length - NonceSize - TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            data.AsSpan(0, NonceSize),
            data.AsSpan(NonceSize + TagSize),
            data.AsSpan(NonceSize, TagSize),
            plaintext);

        return plaintext;
    }

    public ValueConverter<string, byte[]> StringConverter()
    {
        return new ValueConverter<string, byte[]>(
            value => Protect(Encoding.UTF8.GetBytes(value)),
            data => Encoding.UTF8.GetString(Unprotect(data)));
    }
}
