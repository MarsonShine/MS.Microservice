using System.Text;
using MS.Microservice.Core.Domain;

namespace MS.Microservice.Reference.Domain;

public sealed class ProfileValidationException(string message) : Exception(message);
public sealed record ProfileChanged(Guid Id, Guid ProfileId, int Version, string Action, DateTimeOffset OccurredAtUtc);

public static class ProfileRoles
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { "reader", "editor", "administrator" });
}

public sealed class UserProfile : IAggregateRoot
{
    private readonly List<ProfileRole> _roles = [];
    private readonly List<ProfileChanged> _events = [];
    public Guid Id { get; private set; }
    public string Issuer { get; private set; } = "";
    public string Subject { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public int Version { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<ProfileRole> Roles => _roles.AsReadOnly();
    public IReadOnlyCollection<ProfileChanged> DomainEvents => _events.AsReadOnly();
    private UserProfile() { }

    public static UserProfile Create(string issuer, string subject, string displayName, IEnumerable<string> roles,
        DateTimeOffset now)
    {
        ValidateIdentity(issuer, subject);
        var names = ValidateProfile(displayName, roles, now);
        var profile = new UserProfile
        {
            Id = Guid.CreateVersion7(), Issuer = issuer, Subject = subject,
            DisplayName = displayName.Trim(), Version = 1, UpdatedAtUtc = now
        };
        profile._roles.AddRange(names.Select(name => new ProfileRole(profile.Id, name)));
        profile._events.Add(new(Guid.CreateVersion7(), profile.Id, profile.Version, "profile.created", now));
        return profile;
    }

    public bool Change(string displayName, IEnumerable<string> roles, DateTimeOffset now)
    {
        var names = ValidateProfile(displayName, roles, now);
        var normalizedName = displayName.Trim();
        if (DisplayName == normalizedName && names.SequenceEqual(_roles.Select(role => role.Name).Order(StringComparer.Ordinal)))
            return false;
        var nextVersion = checked(Version + 1);
        DisplayName = normalizedName;
        _roles.RemoveAll(role => !names.Contains(role.Name, StringComparer.Ordinal));
        foreach (var name in names.Where(name => _roles.All(role => role.Name != name))) _roles.Add(new(Id, name));
        Version = nextVersion;
        UpdatedAtUtc = now;
        _events.Add(new(Guid.CreateVersion7(), Id, Version, "profile.updated", now));
        return true;
    }

    public void ClearDomainEvents() => _events.Clear();

    public static void ValidateIdentity(string issuer, string subject)
    {
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject)
            || issuer.Length > 512 || subject.Length > 255
            || Encoding.UTF8.GetByteCount(issuer) + Encoding.UTF8.GetByteCount(subject) > 2000
            || !Uri.TryCreate(issuer, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new ProfileValidationException("A bounded absolute issuer and a nonempty external subject are required.");
    }

    private static string[] ValidateProfile(string displayName, IEnumerable<string> roles, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 200)
            throw new ProfileValidationException("Display name must contain 1..200 characters.");
        if (roles is null || now == default || now.Offset != TimeSpan.Zero)
            throw new ProfileValidationException("Roles and a UTC change time are required.");
        var names = roles.Select(role => role?.Trim().ToLowerInvariant() ?? "").Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        if (names.Any(name => !ProfileRoles.All.Contains(name, StringComparer.Ordinal)))
            throw new ProfileValidationException("The profile contains an unknown business role.");
        return names;
    }
}

public sealed class ProfileRole
{
    public Guid ProfileId { get; private set; }
    public string Name { get; private set; } = "";
    private ProfileRole() { }
    internal ProfileRole(Guid profileId, string name) => (ProfileId, Name) = (profileId, name);
}
