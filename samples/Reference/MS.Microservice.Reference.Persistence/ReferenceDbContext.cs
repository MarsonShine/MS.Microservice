using Microsoft.EntityFrameworkCore;
using MS.Microservice.Idempotency.EFCore;
using MS.Microservice.Messaging.SelfManaged;
using MS.Microservice.Reference.Domain;
using global::Wolverine.EntityFrameworkCore;

namespace MS.Microservice.Reference.Persistence;

public abstract class ReferenceDbContext(DbContextOptions options) : DbContext(options)
{
    public const string Schema = "reference";
    public DbSet<UserProfile> Profiles => Set<UserProfile>();
    public DbSet<ProfileAuditEntry> Audit => Set<ProfileAuditEntry>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema(Schema);
        model.AddHttpIdempotency(Schema);
        var profile = model.Entity<UserProfile>();
        profile.ToTable("UserProfiles");
        profile.HasKey(x => x.Id);
        profile.Property(x => x.Id).ValueGeneratedNever();
        profile.Property(x => x.Issuer).HasMaxLength(512);
        profile.Property(x => x.Subject).HasMaxLength(255);
        profile.Property(x => x.DisplayName).HasMaxLength(200);
        profile.Property(x => x.Version).IsConcurrencyToken();
        profile.Property(x => x.UpdatedAtUtc).HasConversion(value => value.UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
        profile.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
        profile.Ignore(x => x.DomainEvents);
        profile.HasMany(x => x.Roles).WithOne().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        profile.Navigation(x => x.Roles).HasField("_roles").UsePropertyAccessMode(PropertyAccessMode.Field);
        var role = model.Entity<ProfileRole>();
        role.ToTable("ProfileRoles");
        role.HasKey(x => new { x.ProfileId, x.Name });
        role.Property(x => x.Name).HasMaxLength(32);

        var audit = model.Entity<ProfileAuditEntry>();
        audit.ToTable("ProfileAudit");
        audit.HasKey(x => new { x.MessageId, x.Consumer });
        audit.Property(x => x.MessageId).ValueGeneratedNever();
        audit.Property(x => x.Consumer).HasMaxLength(200);
        audit.Property(x => x.Action).HasMaxLength(32);
        audit.Property(x => x.ActorIssuer).HasMaxLength(512);
        audit.Property(x => x.ActorSubject).HasMaxLength(255);
        audit.Property(x => x.OccurredAtUtc).HasConversion(value => value.UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
        audit.HasIndex(x => new { x.ProfileId, x.ProfileVersion, x.Consumer }).IsUnique();
    }
}

public sealed class SelfManagedReferenceDbContext(DbContextOptions<SelfManagedReferenceDbContext> options) : ReferenceDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.AddSelfManagedMessaging();
    }
}

public sealed class WolverineReferenceDbContext(DbContextOptions<WolverineReferenceDbContext> options) : ReferenceDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.MapWolverineEnvelopeStorage("wolverine");
    }
}
