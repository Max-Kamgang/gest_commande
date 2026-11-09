using System.Security.Cryptography;

namespace GestionCommandes.Shared.Security;

/// <summary>
/// Hachage de mot de passe PBKDF2-HMAC-SHA256 : sel aleatoire de 16 octets,
/// 210 000 iterations, cle de 32 octets. Format stocke :
/// <c>pbkdf2$iterations$sel$empreinte</c> (base 64).
/// Aucun mot de passe n'est jamais stocke ni journalise en clair.
/// </summary>
public static class PasswordHasher
{
    private const string Prefix = "pbkdf2";
    private const int Iterations = 210_000;

    /// <summary>Produit l'empreinte d'un mot de passe.</summary>
    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);

        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    /// <summary>Verifie un mot de passe, en temps constant.</summary>
    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('$');

        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
