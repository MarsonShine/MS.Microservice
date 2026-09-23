using System.Security.Cryptography;
using System.Text;
using MS.Microservice.Core.Security.Cryptology;
using MS.Microservice.Domain.Aggregates.IdentityModel;

namespace MS.Microservice.Lab.Application.Identity;

// This verifier exists only while stored HMAC password hashes are being upgraded at login.
internal static class LegacyPasswordVerifier
{
    private const string HistoricalKey = "QaP1AF8utIarcBqdhYTZpVGbiNQ9M6IL";

    internal static bool Verify(User user, string providedPassword)
    {
        if (string.IsNullOrEmpty(user.Salt)
            || string.Equals(user.Salt, User.ModernPasswordSaltMarker, StringComparison.Ordinal)
            || string.IsNullOrEmpty(user.Password))
        {
            return false;
        }

        var expectedHash = CryptologyHelper.HmacSha256(providedPassword + user.Salt, HistoricalKey);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHash);
        var actualBytes = Encoding.UTF8.GetBytes(user.Password);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
