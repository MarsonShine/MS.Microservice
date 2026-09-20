using System.Text.Json;
using System.Text.Json.Serialization;
using MS.Microservice.Messaging;
using MS.Microservice.Reference.Domain;

namespace MS.Microservice.Reference.Application;

public sealed record UserProfileChangedV1(Guid Id, DateTimeOffset OccurredAtUtc, Guid ProfileId, int Version,
    string Action, string ActorIssuer, string ActorSubject) : IIntegrationEvent;

public static class ReferenceMessages
{
    public static MessageTopology Topology() => new(
        [MessageContract.For<UserProfileChangedV1>("reference.profile.changed", ReferenceMessageJsonContext.Default.UserProfileChangedV1)],
        [MessageSubscription.For<UserProfileChangedV1, ProfileAuditHandler>("profile-audit")]);
}

public sealed class ProfileAuditHandler(IProfileAuditRepository audits) : IIntegrationEventHandler<UserProfileChangedV1>
{
    public async Task HandleAsync(UserProfileChangedV1 message, MessageContext context, CancellationToken cancellationToken)
    {
        if (await audits.HasEffectAsync(message.ProfileId, message.Version, context.Consumer, cancellationToken)) return;
        try
        {
            audits.Add(new(message.Id, context.Consumer, message.ProfileId, message.Version, message.Action,
                message.ActorIssuer, message.ActorSubject, message.OccurredAtUtc));
        }
        catch (ProfileValidationException) { throw new PermanentMessageException("invalid_profile_change"); }
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(UserProfileChangedV1))]
internal partial class ReferenceMessageJsonContext : JsonSerializerContext;
