using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using MS.Microservice.Core.Domain.Entity;
using MS.Microservice.Core.Domain.Repository;
using MS.Microservice.Domain;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Domain.Aggregates.LogAggregate;
using MS.Microservice.Domain.Events;
using MS.Microservice.Persistence.EFCore.EntityConfigurations;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Action = MS.Microservice.Domain.Aggregates.IdentityModel.Action;
using EfCoreDbContext = Microsoft.EntityFrameworkCore.DbContext;

namespace MS.Microservice.Persistence.EFCore.DbContext
{
    public class ActivationDbContext : EfCoreDbContext, IUnitOfWork
    {
        public const string DEFAULT_SCHEMA = "fz_platform_activation";

        [NotNull]
        public DbSet<User>? Users { get; set; }

        [NotNull]
        public DbSet<Role>? Roles { get; set; }

        [NotNull]
        public DbSet<Action>? Actions { get; set; }

        [NotNull]
        public DbSet<RoleAction>? RoleActions { get; set; }

        [NotNull]
        public DbSet<LogAggregateRoot>? Logs { get; set; }

        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

        private readonly MsPlatformDbContextSettings _platformDbContextOption;
        private static readonly JsonSerializerOptions OutboxSerializerOptions = new(JsonSerializerDefaults.Web);

        public ActivationDbContext(
            DbContextOptions<ActivationDbContext> dbContextOptions,
            IOptions<MsPlatformDbContextSettings> settingsOptions,
            IDomainEventDispatcher domainEventDispatcher) : base(dbContextOptions)
        {
            ArgumentNullException.ThrowIfNull(domainEventDispatcher);
            _platformDbContextOption = settingsOptions?.Value ?? throw new ArgumentNullException(nameof(settingsOptions));
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (_platformDbContextOption.EnableSensitiveDataLogging)
            {
                optionsBuilder.EnableSensitiveDataLogging();
            }

            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(DEFAULT_SCHEMA);

            if (_platformDbContextOption.EnabledSoftDeleted)
            {
                EnableSoftDeletedQueryFilter(modelBuilder);
            }

            modelBuilder.ApplyConfiguration(new LogEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new IdentityUserEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new IdentityRoleEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new IdentityActionEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new InboxMessageEntityTypeConfiguration());

            SettingDatetimePrecision(modelBuilder);
        }

        private static void SettingDatetimePrecision(ModelBuilder modelBuilder)
        {
            var properties = modelBuilder.Model.GetEntityTypes()
                .SelectMany(t => t.GetProperties());

            foreach (var property in properties
                .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)))
            {
                property.SetPrecision(0);
            }
        }

        private static void EnableSoftDeletedQueryFilter(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(ISoftDeleted).IsAssignableFrom(entityType.ClrType))
                {
                    entityType.AddSoftDeletedQueryFilter();
                }
            }
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_platformDbContextOption.EnabledAutoTimeTracker())
            {
                var entries = ChangeTracker.Entries()
                    .Where(e => e.Entity is ICreatedAt || e.Entity is IUpdatedAt)
                    .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

                foreach (var entityEntry in entries)
                {
                    if (entityEntry.Entity is IUpdatedAt updatedAt)
                    {
                        updatedAt.UpdatedAt = DateTime.Now;
                    }

                    if (entityEntry.State == EntityState.Added)
                    {
                        ((ICreatedAt)entityEntry.Entity).CreatedAt = DateTime.Now;
                    }
                }
            }

            var domainEntities = ChangeTracker
                .Entries()
                .Select(entry => entry.Entity)
                .OfType<IHasDomainEvents>()
                .Where(entity => entity.DomainEvents.Count != 0)
                .ToList();
            var domainEvents = domainEntities
                .SelectMany(entity => entity.DomainEvents)
                .ToList();
            var outboxMessages = domainEvents
                .Select(CreateOutboxMessage)
                .ToList();

            if (outboxMessages.Count != 0)
            {
                OutboxMessages.AddRange(outboxMessages);
            }

            try
            {
                var result = await base.SaveChangesAsync(cancellationToken);
                domainEntities.ForEach(entity => entity.ClearDomainEvents());
                return result;
            }
            catch
            {
                foreach (var outboxMessage in outboxMessages)
                {
                    Entry(outboxMessage).State = EntityState.Detached;
                }

                throw;
            }
        }

        public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
        {
            await SaveChangesAsync(cancellationToken);
            return true;
        }

        private static OutboxMessage CreateOutboxMessage(IDomainEvent domainEvent)
        {
            var eventType = domainEvent.GetType();
            var messageType = eventType.AssemblyQualifiedName
                ?? throw new InvalidOperationException($"Domain event type '{eventType}' has no assembly-qualified name.");
            var payload = JsonSerializer.Serialize(domainEvent, eventType, OutboxSerializerOptions);
            var activity = Activity.Current;
            return OutboxMessage.Create(
                messageType,
                payload,
                DateTimeOffset.UtcNow,
                traceId: activity?.TraceId.ToString(),
                correlationId: activity?.GetBaggageItem("correlationId") ?? activity?.RootId);
        }

        private IDbContextTransaction? _currentTransaction;

        public async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (_currentTransaction != null)
            {
                return null;
            }

            _currentTransaction = await Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
            return _currentTransaction;
        }

        public async Task CommitTransactionAsync([NotNull] IDbContextTransaction transaction, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(transaction);
            if (transaction != _currentTransaction) throw new InvalidOperationException($"Transaction {transaction.TransactionId} is not current");

            try
            {
                await SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception transactionException)
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "The transaction failed and could not be rolled back.",
                        transactionException,
                        rollbackException);
                }

                throw;
            }
            finally
            {
                if (_currentTransaction != null)
                {
                    await _currentTransaction.DisposeAsync();
                    _currentTransaction = null;
                }
            }
        }
    }

    public class ActivationDbContextDesignFactory : IDesignTimeDbContextFactory<ActivationDbContext>
    {
        public ActivationDbContext CreateDbContext(string[] args)
        {
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ActivationConnection")
                ?? "Host=localhost;Database=activation_design;Username=postgres;Password=postgres";
            var builder = new DbContextOptionsBuilder<ActivationDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__MigrationsHistory", ActivationDbContext.DEFAULT_SCHEMA));

            return new ActivationDbContext(
                builder.Options,
                Options.Create(new MsPlatformDbContextSettings()),
                new NoOpDomainEventDispatcher());
        }
    }
}
