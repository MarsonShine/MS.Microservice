using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Consts;
using MS.Microservice.Persistence.EFCore.DbContext;

namespace MS.Microservice.Lab.Persistence;

public static class LabIdentitySeed
{
    public static async Task SeedAsync(ActivationDbContext context, string operatorPassword, string readerPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operatorPassword) || operatorPassword.Length < 16
            || string.IsNullOrWhiteSpace(readerPassword) || readerPassword.Length < 16)
            throw new ArgumentException("Lab bootstrap passwords must contain at least 16 characters.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var role = await context.Roles.Include(x => x.Actions).SingleOrDefaultAsync(x => x.Name == "lab-message-operator", cancellationToken);
        if (role is null)
        {
            role = new Role("lab-message-operator", "Lab message operations");
            role.AddAction("Message operations", LabPermissions.MessagingOperations);
            context.Roles.Add(role);
        }
        await AddIfMissing("lab-operator", operatorPassword, role);
        await AddIfMissing("lab-reader", readerPassword, null);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        async Task AddIfMissing(string account, string password, Role? assignedRole)
        {
            if (await context.Users.IgnoreQueryFilters().AnyAsync(x => x.Account == account, cancellationToken)) return;
            var user = new User(account, "", "", false, "", 0, 0, account + "@example.invalid", account, "lab:" + account, "lab:" + account)
                { CreatedAt = DateTime.UtcNow };
            user.SetPasswordHash(new PasswordHasher<User>().HashPassword(user, password));
            if (assignedRole is not null) user.AddRole(assignedRole);
            context.Users.Add(user);
        }
    }
}
