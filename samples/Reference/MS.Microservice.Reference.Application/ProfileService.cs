using MS.Microservice.Core.Functional;
using MS.Microservice.Messaging;
using MS.Microservice.Reference.Domain;
using static MS.Microservice.Core.Functional.F;

namespace MS.Microservice.Reference.Application;

public sealed class ProfileService(IProfileRepository profiles, IUnitOfWork unit,
    IIntegrationEventPublisher publisher, TimeProvider clock)
{
    public async Task<Either<Error, ProfileView>> CreateAsync(CreateProfile request, AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        try
        {
            UserProfile.ValidateIdentity(actor.Issuer, actor.Subject);
            var profile = UserProfile.Create(request.Issuer, request.Subject, request.DisplayName, request.Roles, clock.GetUtcNow());
            var result = await unit.ExecuteAsync<Outcome>(async token =>
            {
                if (await profiles.FindAsync(profile.Issuer, profile.Subject, token) is not null)
                    return Error.Conflict("A profile already exists for this external identity.");
                profiles.Add(profile);
                await EnqueueChangesAsync(profile, actor, token);
                return ProfileView.From(profile);
            }, cancellationToken);
            if (result is ProfileView) profile.ClearDomainEvents();
            return ToEither(result);
        }
        catch (ProfileValidationException exception) { return ToEither(Error.Validation(exception.Message)); }
    }

    public async Task<Either<Error, ProfileView>> ChangeAsync(Guid id, ChangeProfile request, AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        if (request.ExpectedVersion < 1) return ToEither(Error.Validation("ExpectedVersion must be positive."));
        UserProfile? changed = null;
        try
        {
            UserProfile.ValidateIdentity(actor.Issuer, actor.Subject);
            var result = await unit.ExecuteAsync<Outcome>(async token =>
            {
                var profile = await profiles.GetAsync(id, token);
                if (profile is null) return Error.NotFound("Profile not found.");
                if (profile.Version != request.ExpectedVersion) return Error.Conflict("Profile changed; reload its current version.");
                changed = profile;
                if (profile.Change(request.DisplayName, request.Roles, clock.GetUtcNow()))
                    await EnqueueChangesAsync(profile, actor, token);
                return ProfileView.From(profile);
            }, cancellationToken);
            if (result is ProfileView) changed?.ClearDomainEvents();
            return ToEither(result);
        }
        catch (ProfileValidationException exception) { return ToEither(Error.Validation(exception.Message)); }
    }

    private union Outcome(Error, ProfileView);

    private static Either<Error, ProfileView> ToEither(Outcome result) => result switch
    {
        Error error => Left(error),
        ProfileView profile => Right(profile),
        null => throw new InvalidOperationException("A profile operation returned no result.")
    };

    private async Task EnqueueChangesAsync(UserProfile profile, AuditActor actor, CancellationToken cancellationToken)
    {
        foreach (var change in profile.DomainEvents)
            await publisher.EnqueueAsync(new UserProfileChangedV1(change.Id, change.OccurredAtUtc, change.ProfileId,
                change.Version, change.Action, actor.Issuer, actor.Subject), cancellationToken);
    }
}
