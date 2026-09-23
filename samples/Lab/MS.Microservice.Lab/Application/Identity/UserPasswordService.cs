using Microsoft.AspNetCore.Identity;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Services.Interfaces;

namespace MS.Microservice.Lab.Application.Identity;

public interface IUserPasswordService
{
    string HashPassword(User user, string password);

    Task<bool> VerifyAndUpgradeAsync(
        User user,
        string providedPassword,
        CancellationToken cancellationToken = default);
}

public sealed class UserPasswordService(
    IPasswordHasher<User> passwordHasher,
    IUserDomainService userDomainService) : IUserPasswordService
{
    private readonly IPasswordHasher<User> _passwordHasher = passwordHasher
        ?? throw new ArgumentNullException(nameof(passwordHasher));
    private readonly IUserDomainService _userDomainService = userDomainService
        ?? throw new ArgumentNullException(nameof(userDomainService));

    public string HashPassword(User user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return _passwordHasher.HashPassword(user, password);
    }

    public async Task<bool> VerifyAndUpgradeAsync(
        User user,
        string providedPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrEmpty(providedPassword) || string.IsNullOrWhiteSpace(user.Password))
        {
            return false;
        }

        var verificationResult = VerifyModernPassword(user, user.Password, providedPassword);
        if (verificationResult == PasswordVerificationResult.Failed
            && LegacyPasswordVerifier.Verify(user, providedPassword))
        {
            verificationResult = PasswordVerificationResult.SuccessRehashNeeded;
        }

        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return false;
        }

        if (verificationResult == PasswordVerificationResult.Success)
        {
            return true;
        }

        var upgradedHash = HashPassword(user, providedPassword);
        return await _userDomainService.UpdatePasswordHashAsync(user, upgradedHash, cancellationToken);
    }

    private PasswordVerificationResult VerifyModernPassword(
        User user,
        string passwordHash,
        string providedPassword)
    {
        try
        {
            return _passwordHasher.VerifyHashedPassword(user, passwordHash, providedPassword);
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }
    }

}
