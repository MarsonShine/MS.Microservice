using MS.Microservice.Reference.Domain;

namespace MS.Microservice.Reference.Application;

public sealed record AuditActor(string Issuer, string Subject);
public sealed record CreateProfile(string Issuer, string Subject, string DisplayName, string[] Roles);
public sealed record ChangeProfile(string DisplayName, string[] Roles, int ExpectedVersion);
public sealed record ProfileView(Guid Id, string Issuer, string Subject, string DisplayName, string[] Roles,
    int Version, DateTimeOffset UpdatedAtUtc)
{
    public static ProfileView From(UserProfile profile) => new(profile.Id, profile.Issuer, profile.Subject,
        profile.DisplayName, profile.Roles.Select(role => role.Name).Order(StringComparer.Ordinal).ToArray(),
        profile.Version, profile.UpdatedAtUtc);
}

public interface IProfileRepository
{
    Task<UserProfile?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<UserProfile?> FindAsync(string issuer, string subject, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserProfile>> ListAsync(int skip, int take, CancellationToken cancellationToken);
    void Add(UserProfile profile);
}

public interface IProfileAuditRepository
{
    Task<bool> HasEffectAsync(Guid profileId, int version, string consumer, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProfileAuditEntry>> ListAsync(Guid? profileId, int take, CancellationToken cancellationToken);
    void Add(ProfileAuditEntry audit);
}
