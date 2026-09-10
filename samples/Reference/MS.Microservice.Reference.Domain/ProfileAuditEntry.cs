namespace MS.Microservice.Reference.Domain;

public sealed class ProfileAuditEntry
{
    public Guid MessageId { get; private set; }
    public string Consumer { get; private set; } = "";
    public Guid ProfileId { get; private set; }
    public int ProfileVersion { get; private set; }
    public string Action { get; private set; } = "";
    public string ActorIssuer { get; private set; } = "";
    public string ActorSubject { get; private set; } = "";
    public DateTimeOffset OccurredAtUtc { get; private set; }
    private ProfileAuditEntry() { }

    public ProfileAuditEntry(Guid messageId, string consumer, Guid profileId, int version, string action,
        string actorIssuer, string actorSubject, DateTimeOffset occurredAtUtc)
    {
        UserProfile.ValidateIdentity(actorIssuer, actorSubject);
        if (messageId == Guid.Empty || profileId == Guid.Empty || version < 1 || string.IsNullOrWhiteSpace(consumer)
            || action is not ("profile.created" or "profile.updated") || occurredAtUtc == default || occurredAtUtc.Offset != TimeSpan.Zero)
            throw new ProfileValidationException("Invalid profile audit event.");
        (MessageId, Consumer, ProfileId, ProfileVersion, Action, ActorIssuer, ActorSubject, OccurredAtUtc)
            = (messageId, consumer, profileId, version, action, actorIssuer, actorSubject, occurredAtUtc);
    }
}
