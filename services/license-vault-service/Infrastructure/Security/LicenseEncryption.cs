
using System.Security.Cryptography;
using System.Text;
using LicenseVault.Api.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace LicenseVault.Api.Infrastructure.Security;

public sealed class LicenseEncryption : ILicenseEncryption
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const byte FormatVersion = 1;

    private readonly byte[] _encryptionKey;
    private readonly byte[] _fingerprintKey;

    public LicenseEncryption(
        IOptions<LicenseEncryptionOptions> options)
    {
        _encryptionKey = ReadKey(
            options.Value.EncryptionKey,
            "EncryptionKey");

        _fingerprintKey = ReadKey(
            options.Value.FingerprintKey,
            "FingerprintKey");

        if (CryptographicOperations.FixedTimeEquals(
            _encryptionKey, _fingerprintKey))
        {
            throw new InvalidOperationException(
                "EncryptionKey and FingerprintKey must be different.");
        }
    }

    public string Encrypt(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);

        var data = Encoding.UTF8.GetBytes(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[data.Length];

        try
        {
            using var aes = new AesGcm(
                _encryptionKey, TagSize);

            aes.Encrypt(
                nonce,
                data,
                ciphertext,
                tag,
                new byte[] { FormatVersion });

            var result = new byte[
                1 + NonceSize + TagSize + ciphertext.Length
            ];

            result[0] = FormatVersion;

            nonce.CopyTo(result, 1);

            tag.CopyTo(result, 1 + NonceSize);

            ciphertext.CopyTo(
                result,
                1 + NonceSize + TagSize);

            return Convert.ToBase64String(result);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }

    public string Decrypt(string encrypted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encrypted);

        var data = Convert.FromBase64String(encrypted);

        if (data.Length <= 1 + NonceSize + TagSize ||
            data[0] != FormatVersion)
        {
            throw new CryptographicException(
                "Invalid encrypted license format.");
        }

        var nonce = data.AsSpan(1, NonceSize);

        var tag = data.AsSpan(
            1 + NonceSize, TagSize);

        var ciphertext = data.AsSpan(
            1 + NonceSize + TagSize);

        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(
                _encryptionKey, TagSize);

            aes.Decrypt(
                nonce,
                ciphertext,
                tag,
                plaintext,
                new byte[] { FormatVersion });

            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public string Fingerprint(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);

        var data = Encoding.UTF8.GetBytes(plaintext.Trim());

        try
        {
            var hash = HMACSHA256.HashData(
                _fingerprintKey, data);

            return Convert.ToHexString(hash)
                .ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }

    private static byte[] ReadKey(
        string value,
        string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{name} is missing.");
        }

        byte[] key;

        try
        {
            key = Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"{name} must be valid Base64.", ex);
        }

        if (key.Length != KeySize)
        {
            throw new InvalidOperationException(
                $"{name} must contain exactly 32 bytes.");
        }

        return key;
    }
}
