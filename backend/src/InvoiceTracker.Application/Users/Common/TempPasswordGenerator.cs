using System.Security.Cryptography;

namespace InvoiceTracker.Application.Users.Common;

/// <summary>
/// Generates a random temporary password for a newly created Staff user, satisfying the
/// Identity password policy configured in <c>Infrastructure/DependencyInjection.cs</c>
/// (RequiredLength = 8, RequireUppercase/RequireLowercase/RequireDigit = true (Identity
/// defaults, left unchanged), RequireNonAlphanumeric = false). The Owner retrieves it via the
/// (currently no-op/logging) <c>IEmailSender</c> and hands it to the new Staff user out of band.
/// </summary>
public static class TempPasswordGenerator
{
    private const string UppercaseChars = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string LowercaseChars = "abcdefghijkmnopqrstuvwxyz";
    private const string DigitChars = "23456789";
    private const string AllChars = UppercaseChars + LowercaseChars + DigitChars;

    public static string Generate(int length = 12)
    {
        if (length < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Temp password must be at least 8 characters to satisfy the password policy.");
        }

        var passwordChars = new char[length];

        // Guarantee at least one of each required character class first...
        passwordChars[0] = PickRandomChar(UppercaseChars);
        passwordChars[1] = PickRandomChar(LowercaseChars);
        passwordChars[2] = PickRandomChar(DigitChars);

        // ...then fill the remainder from the full alphabet.
        for (var i = 3; i < length; i++)
        {
            passwordChars[i] = PickRandomChar(AllChars);
        }

        Shuffle(passwordChars);

        return new string(passwordChars);
    }

    private static char PickRandomChar(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    private static void Shuffle(char[] chars)
    {
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[swapIndex]) = (chars[swapIndex], chars[i]);
        }
    }
}
