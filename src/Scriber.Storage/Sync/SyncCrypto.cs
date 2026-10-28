namespace Scriber.Storage.Sync;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Client-side zero-knowledge payload encryption for remote sync bundles (ADR-0007).
/// Uses AES-256 with HMAC-SHA256 authenticated encryption and PBKDF2 key derivation.
/// </summary>
public static class SyncCrypto
{
    private static readonly byte[] HeaderMagic = "SCRE"u8.ToArray();
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int HmacSize = 32;
    private const int Pbkdf2Iterations = 100_000;

    public static byte[] Encrypt(byte[] plainData, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(plainData);
        if (string.IsNullOrEmpty(passphrase))
        {
            throw new ArgumentException("Encryption passphrase cannot be empty.", nameof(passphrase));
        }

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] iv = RandomNumberGenerator.GetBytes(IvSize);

        // Derive 64 bytes: 32 bytes for AES-256 key, 32 bytes for HMAC-SHA256 key
        byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            64);

        byte[] encKey = derivedKey[..32];
        byte[] hmacKey = derivedKey[32..64];

        byte[] cipherText;
        using (var aes = Aes.Create())
        {
            aes.Key = encKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var encryptor = aes.CreateEncryptor();
            cipherText = encryptor.TransformFinalBlock(plainData, 0, plainData.Length);
        }

        // Compute HMAC over (Salt + IV + Ciphertext)
        byte[] macPayload = new byte[salt.Length + iv.Length + cipherText.Length];
        Buffer.BlockCopy(salt, 0, macPayload, 0, salt.Length);
        Buffer.BlockCopy(iv, 0, macPayload, salt.Length, iv.Length);
        Buffer.BlockCopy(cipherText, 0, macPayload, salt.Length + iv.Length, cipherText.Length);

        byte[] hmac;
        using (var hmacSha = new HMACSHA256(hmacKey))
        {
            hmac = hmacSha.ComputeHash(macPayload);
        }

        // Final payload: Magic(4) + Salt(16) + IV(16) + HMAC(32) + CipherText(N)
        byte[] result = new byte[HeaderMagic.Length + SaltSize + IvSize + HmacSize + cipherText.Length];
        int offset = 0;

        Buffer.BlockCopy(HeaderMagic, 0, result, offset, HeaderMagic.Length);
        offset += HeaderMagic.Length;

        Buffer.BlockCopy(salt, 0, result, offset, salt.Length);
        offset += salt.Length;

        Buffer.BlockCopy(iv, 0, result, offset, iv.Length);
        offset += iv.Length;

        Buffer.BlockCopy(hmac, 0, result, offset, hmac.Length);
        offset += hmac.Length;

        Buffer.BlockCopy(cipherText, 0, result, offset, cipherText.Length);

        return result;
    }

    public static byte[] Decrypt(byte[] encryptedData, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(encryptedData);
        if (string.IsNullOrEmpty(passphrase))
        {
            throw new ArgumentException("Encryption passphrase cannot be empty.", nameof(passphrase));
        }

        int minHeaderSize = HeaderMagic.Length + SaltSize + IvSize + HmacSize;
        if (encryptedData.Length < minHeaderSize)
        {
            throw new CryptographicException("Payload too short to be a valid encrypted bundle.");
        }

        for (int i = 0; i < HeaderMagic.Length; i++)
        {
            if (encryptedData[i] != HeaderMagic[i])
            {
                throw new CryptographicException("Invalid encrypted payload header magic.");
            }
        }

        int offset = HeaderMagic.Length;

        byte[] salt = new byte[SaltSize];
        Buffer.BlockCopy(encryptedData, offset, salt, 0, SaltSize);
        offset += SaltSize;

        byte[] iv = new byte[IvSize];
        Buffer.BlockCopy(encryptedData, offset, iv, 0, IvSize);
        offset += IvSize;

        byte[] expectedHmac = new byte[HmacSize];
        Buffer.BlockCopy(encryptedData, offset, expectedHmac, 0, HmacSize);
        offset += HmacSize;

        int cipherTextLen = encryptedData.Length - offset;
        byte[] cipherText = new byte[cipherTextLen];
        Buffer.BlockCopy(encryptedData, offset, cipherText, 0, cipherTextLen);

        // Derive keys
        byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            64);

        byte[] encKey = derivedKey[..32];
        byte[] hmacKey = derivedKey[32..64];

        // Verify HMAC
        byte[] macPayload = new byte[salt.Length + iv.Length + cipherText.Length];
        Buffer.BlockCopy(salt, 0, macPayload, 0, salt.Length);
        Buffer.BlockCopy(iv, 0, macPayload, salt.Length, iv.Length);
        Buffer.BlockCopy(cipherText, 0, macPayload, salt.Length + iv.Length, cipherText.Length);

        using (var hmacSha = new HMACSHA256(hmacKey))
        {
            byte[] computedHmac = hmacSha.ComputeHash(macPayload);
            if (!CryptographicOperations.FixedTimeEquals(computedHmac, expectedHmac))
            {
                throw new CryptographicException("Authentication failed: invalid passphrase or corrupted payload.");
            }
        }

        // Decrypt
        using var aes = Aes.Create();
        aes.Key = encKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
    }
}
