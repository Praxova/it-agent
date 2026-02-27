using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using LucidAdmin.Core.Interfaces.Services;

namespace LucidAdmin.Infrastructure.Services;

public class Argon2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;

    // Argon2id parameters — stronger than OWASP minimum (64MB/3 iter/4 par)
    // Chosen for consistency with SealManager's KEK derivation.
    // Higher memory (128MB) provides better GPU resistance; lower parallelism
    // trades multi-core scaling for predictable memory footprint on server hardware.
    private const int Iterations = 4;
    private const int MemorySize = 128 * 1024; // 128 MB
    private const int DegreeOfParallelism = 2;

    public string HashPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = HashPasswordWithSalt(password, salt);

        var combined = new byte[SaltSize + HashSize];
        Buffer.BlockCopy(salt, 0, combined, 0, SaltSize);
        Buffer.BlockCopy(hash, 0, combined, SaltSize, HashSize);

        return Convert.ToBase64String(combined);
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(passwordHash);

        var combined = Convert.FromBase64String(passwordHash);
        if (combined.Length != SaltSize + HashSize)
        {
            return false;
        }

        var salt = new byte[SaltSize];
        var hash = new byte[HashSize];
        Buffer.BlockCopy(combined, 0, salt, 0, SaltSize);
        Buffer.BlockCopy(combined, SaltSize, hash, 0, HashSize);

        var computedHash = HashPasswordWithSalt(password, salt);
        return CryptographicOperations.FixedTimeEquals(hash, computedHash);
    }

    private static byte[] HashPasswordWithSalt(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = DegreeOfParallelism,
            MemorySize = MemorySize,
            Iterations = Iterations
        };

        return argon2.GetBytes(HashSize);
    }
}
