using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Domain;

namespace MS.Microservice.Reference.Persistence;

public sealed class ProfileRepository(ReferenceDbContext context) : IProfileRepository
{
    public Task<UserProfile?> GetAsync(Guid id, CancellationToken cancellationToken)
        => context.Profiles.Include(x => x.Roles).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<UserProfile?> FindAsync(string issuer, string subject, CancellationToken cancellationToken)
        => context.Profiles.Include(x => x.Roles).SingleOrDefaultAsync(x => x.Issuer == issuer && x.Subject == subject, cancellationToken);
    public async Task<IReadOnlyList<UserProfile>> ListAsync(int skip, int take, CancellationToken cancellationToken)
    {
        if (skip < 0 || take is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(take));
        return await context.Profiles.AsNoTracking().Include(x => x.Roles).OrderBy(x => x.Id)
            .Skip(skip).Take(take).ToListAsync(cancellationToken);
    }
    public void Add(UserProfile profile) => context.Profiles.Add(profile);
}

public sealed class ProfileAuditRepository(ReferenceDbContext context) : IProfileAuditRepository
{
    public Task<bool> HasEffectAsync(Guid profileId, int version, string consumer, CancellationToken cancellationToken)
        => context.Audit.AnyAsync(x => x.ProfileId == profileId && x.ProfileVersion == version && x.Consumer == consumer, cancellationToken);
    public async Task<IReadOnlyList<ProfileAuditEntry>> ListAsync(Guid? profileId, int take, CancellationToken cancellationToken)
    {
        if (take is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(take));
        var query = context.Audit.AsNoTracking();
        if (profileId.HasValue) query = query.Where(x => x.ProfileId == profileId);
        return await query.OrderByDescending(x => x.OccurredAtUtc).ThenBy(x => x.MessageId).Take(take).ToListAsync(cancellationToken);
    }
    public void Add(ProfileAuditEntry audit) => context.Audit.Add(audit);
}

public static class ReferencePersistenceExtensions
{
    public static IServiceCollection AddReferenceRepositories<TContext>(this IServiceCollection services) where TContext : ReferenceDbContext
    {
        services.AddScoped<ReferenceDbContext>(provider => provider.GetRequiredService<TContext>());
        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<IProfileAuditRepository, ProfileAuditRepository>();
        services.AddScoped<ProfileService>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
