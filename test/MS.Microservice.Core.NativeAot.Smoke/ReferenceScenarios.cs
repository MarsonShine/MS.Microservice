using MS.Microservice.Messaging;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Domain;

namespace MS.Microservice.Core.NativeAot.Smoke;

internal static class ReferenceScenarios
{
    private const string Issuer = "https://issuer.example";
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);

    public static readonly (string Name, Func<Task> Run)[] All =
    [
        ("reference-domain-profile", DomainProfile),
        ("reference-application-write", ApplicationWrite),
        ("reference-application-failures", ApplicationFailures),
        ("reference-message-contract", MessageContract),
        ("reference-audit-business-key", AuditBusinessKey)
    ];

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static async Task Throws<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static Task DomainProfile()
    {
        var profile = UserProfile.Create(Issuer, "opaque-subject", " 用户 ", ["EDITOR", "reader", "editor"], OccurredAt);
        Check(profile.Id != Guid.Empty && profile.Subject == "opaque-subject" && profile.DisplayName == "用户",
            "Profile identity or display normalization changed.");
        Check(profile.Roles.Select(x => x.Name).SequenceEqual(["editor", "reader"]), "Business roles lost their canonical order.");
        Check(profile.Version == 1 && profile.DomainEvents.Single().Action == "profile.created", "Create did not record one domain event.");
        profile.ClearDomainEvents();
        Check(!profile.Change(" 用户 ", ["reader", "EDITOR"], OccurredAt), "No-op change was not recognized.");
        Check(profile.Version == 1 && profile.DomainEvents.Count == 0, "No-op change raised an event or advanced the version.");
        Check(profile.Change("新名字", ["reader"], OccurredAt), "Real change was ignored.");
        Check(profile.Version == 2 && profile.DomainEvents.Single().Action == "profile.updated", "Real change lost its version or event.");
        try { profile.Change("invalid", ["unknown"], OccurredAt); }
        catch (ProfileValidationException)
        {
            Check(profile.Version == 2 && profile.DisplayName == "新名字" && profile.DomainEvents.Count == 1,
                "Invalid change mutated the aggregate.");
            return Task.CompletedTask;
        }
        throw new InvalidOperationException("Unknown business role was accepted.");
    }

    private static async Task ApplicationWrite()
    {
        var profiles = new Profiles();
        var unit = new Unit();
        var publisher = new Publisher();
        var service = new ProfileService(profiles, unit, publisher, TimeProvider.System);
        var actor = new AuditActor(Issuer, "admin");
        var created = await service.CreateAsync(new(Issuer, "subject", "name", ["reader"]), actor);
        Check(created.IsRight && created.Right.Version == 1 && unit.Commits == 1, "Profile creation did not commit.");
        Check(profiles.Added!.DomainEvents.Count == 0 && publisher.Messages.Count == 1,
            "Committed create did not clear the domain event or stage one integration event.");
        var firstEvent = (UserProfileChangedV1)publisher.Messages[0];
        Check(firstEvent.ProfileId == profiles.Added.Id && firstEvent.Action == "profile.created" && firstEvent.Version == 1,
            "Created event does not match the business change.");
        var changed = await service.ChangeAsync(profiles.Added.Id, new("updated", ["editor"], 1), actor);
        Check(changed.IsRight && changed.Right.Version == 2 && unit.Commits == 2 && publisher.Messages.Count == 2,
            "Profile change was not committed and staged once.");
        Check(profiles.Added.DomainEvents.Count == 0 && ((UserProfileChangedV1)publisher.Messages[1]).Action == "profile.updated",
            "Committed change did not clear its event.");
        var noOp = await service.ChangeAsync(profiles.Added.Id, new(" updated ", ["EDITOR", "editor"], 2), actor);
        Check(noOp.IsRight && noOp.Right.Version == 2 && publisher.Messages.Count == 2,
            "No-op update published another event or advanced the version.");
    }

    private static async Task ApplicationFailures()
    {
        var actor = new AuditActor(Issuer, "admin");
        var duplicate = new Profiles();
        duplicate.Items.Add(UserProfile.Create(Issuer, "subject", "existing", [], OccurredAt));
        var publisher = new Publisher();
        var service = new ProfileService(duplicate, new Unit(), publisher, TimeProvider.System);
        var conflict = await service.CreateAsync(new(Issuer, "subject", "new", []), actor);
        Check(conflict.IsLeft && conflict.Left.Code == "conflict" && publisher.Messages.Count == 0,
            "Duplicate identity staged an event or lost its conflict result.");
        var stale = await service.ChangeAsync(duplicate.Items[0].Id, new("changed", [], 2), actor);
        Check(stale.IsLeft && stale.Left.Code == "conflict" && duplicate.Items[0].DisplayName == "existing",
            "Stale version mutated the profile.");
        var invalid = await service.CreateAsync(new(Issuer, "other", "name", ["unknown"]), actor);
        Check(invalid.IsLeft && invalid.Left.Code == "validation", "Invalid role did not return validation error.");

        var uncommitted = new Profiles();
        var staged = new Publisher();
        var failingService = new ProfileService(uncommitted, new Unit(failCommit: true), staged, TimeProvider.System);
        await Throws<IOException>(() => failingService.CreateAsync(new(Issuer, "other", "name", []), actor));
        Check(uncommitted.Added?.DomainEvents.Count == 1 && staged.Messages.Count == 1,
            "Commit failure erased the pending domain event or failed to stage it.");
    }

    private static Task MessageContract()
    {
        var topology = ReferenceMessages.Topology();
        var change = new UserProfileChangedV1(Guid.NewGuid(), OccurredAt, Guid.NewGuid(), 2, "profile.updated", Issuer, "admin");
        var serialized = topology.Registry.Serialize(change);
        Check(serialized.ContractName == "reference.profile.changed" && serialized.ContractVersion == 1,
            "Versioned message contract changed.");
        Check(topology.Registry.Deserialize(serialized) is UserProfileChangedV1 restored && restored == change,
            "Generated message metadata failed the round trip.");
        var subscription = topology.Subscription("profile-audit");
        Check(subscription.MessageType == typeof(UserProfileChangedV1) && subscription.HandlerType == typeof(ProfileAuditHandler),
            "Static message handler registration changed.");
        return Task.CompletedTask;
    }

    private static async Task AuditBusinessKey()
    {
        var audits = new Audits();
        var handler = new ProfileAuditHandler(audits);
        var profileId = Guid.NewGuid();
        var first = new UserProfileChangedV1(Guid.NewGuid(), OccurredAt, profileId, 1, "profile.created", Issuer, "admin");
        await handler.HandleAsync(first, new(first.Id, "audit"), default);
        var duplicate = first with { Id = Guid.NewGuid() };
        await handler.HandleAsync(duplicate, new(duplicate.Id, "audit"), default);
        Check(audits.Entries.Count == 1 && audits.Entries[0].ProfileId == profileId,
            "A repeated business change created another audit effect.");
        var changedVersion = first with { Id = Guid.NewGuid(), Version = 2 };
        await handler.HandleAsync(changedVersion, new(changedVersion.Id, "audit"), default);
        Check(audits.Entries.Count == 2, "A different profile version was suppressed.");
    }

    private sealed class Profiles : IProfileRepository
    {
        public List<UserProfile> Items { get; } = [];
        public UserProfile? Added { get; private set; }
        public Task<UserProfile?> GetAsync(Guid id, CancellationToken token)
            => Task.FromResult(Items.SingleOrDefault(x => x.Id == id));
        public Task<UserProfile?> FindAsync(string issuer, string subject, CancellationToken token)
            => Task.FromResult(Items.SingleOrDefault(x => x.Issuer == issuer && x.Subject == subject));
        public Task<IReadOnlyList<UserProfile>> ListAsync(int skip, int take, CancellationToken token)
            => Task.FromResult<IReadOnlyList<UserProfile>>(Items.Skip(skip).Take(take).ToArray());
        public void Add(UserProfile profile) { Added = profile; Items.Add(profile); }
    }

    private sealed class Unit(bool failCommit = false) : IUnitOfWork
    {
        public int Commits { get; private set; }
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token = default)
        {
            var result = await operation(token);
            if (failCommit) throw new IOException("commit failed");
            Commits++;
            return result;
        }
    }

    private sealed class Publisher : IIntegrationEventPublisher
    {
        public List<IIntegrationEvent> Messages { get; } = [];
        public ValueTask EnqueueAsync(IIntegrationEvent message, CancellationToken token = default)
        { Messages.Add(message); return ValueTask.CompletedTask; }
    }

    private sealed class Audits : IProfileAuditRepository
    {
        public List<ProfileAuditEntry> Entries { get; } = [];
        public Task<bool> HasEffectAsync(Guid profileId, int version, string consumer, CancellationToken token)
            => Task.FromResult(Entries.Any(x => x.ProfileId == profileId && x.ProfileVersion == version && x.Consumer == consumer));
        public Task<IReadOnlyList<ProfileAuditEntry>> ListAsync(Guid? profileId, int take, CancellationToken token)
            => Task.FromResult<IReadOnlyList<ProfileAuditEntry>>(Entries.Where(x => profileId is null || x.ProfileId == profileId).Take(take).ToArray());
        public void Add(ProfileAuditEntry audit) => Entries.Add(audit);
    }
}
