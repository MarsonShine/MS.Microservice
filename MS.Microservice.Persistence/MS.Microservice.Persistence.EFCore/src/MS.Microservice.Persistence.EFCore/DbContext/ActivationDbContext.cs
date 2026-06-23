using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
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
using System.IO;
using System.Linq;
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

        private readonly IDomainEventDispatcher _domainEventDispatcher;
        private readonly MsPlatformDbContextSettings _platformDbContextOption;

        public ActivationDbContext(
            DbContextOptions<ActivationDbContext> dbContextOptions,
            IOptions<MsPlatformDbContextSettings> settingsOptions,
            IDomainEventDispatcher domainEventDispatcher) : base(dbContextOptions)
        {
            _domainEventDispatcher = domainEventDispatcher ?? throw new ArgumentNullException(nameof(domainEventDispatcher));
            _platformDbContextOption = settingsOptions?.Value ?? throw new ArgumentNullException(nameof(settingsOptions));
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.EnableSensitiveDataLogging();
            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (_platformDbContextOption.EnabledSoftDeleted)
            {
                EnableSoftDeletedQueryFilter(modelBuilder);
            }

            modelBuilder.ApplyConfiguration(new LogEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new IdentityUserEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new IdentityRoleEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new IdentityActionEntityTypeConfiguration());

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

            return await base.SaveChangesAsync(cancellationToken);
        }

        public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
        {
            var domainEntities = ChangeTracker
                .Entries()
                .Select(entry => entry.Entity)
                .OfType<IHasDomainEvents>()
                .Where(entity => entity.DomainEvents.Count != 0)
                .ToList();

            var domainEvents = domainEntities
                .SelectMany(entity => entity.DomainEvents)
                .ToList();

            await SaveChangesAsync(cancellationToken);

            if (domainEvents.Count != 0)
            {
                await _domainEventDispatcher.DispatchAsync(domainEvents, cancellationToken);
                domainEntities.ForEach(entity => entity.ClearDomainEvents());
            }

            return true;
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
            catch
            {
                await _currentTransaction.RollbackAsync(cancellationToken);
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
            var configuration = BuildConfiguration();
            var connectionString = configuration.GetConnectionString("ActivationConnection")
                ?? throw new InvalidOperationException("ConnectionStrings:ActivationConnection is required.");
            var builder = new DbContextOptionsBuilder<ActivationDbContext>()
                .UseNpgsql(connectionString);

            var settings = configuration
                .GetSection(MsPlatformDbContextSettings.SectionName)
                .Get<MsPlatformDbContextSettings>() ?? new MsPlatformDbContextSettings();

            return new ActivationDbContext(builder.Options, Options.Create(settings), new NoOpDomainEventDispatcher());
        }

        private static IConfigurationRoot BuildConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../Migrators/"))
                .AddJsonFile("appsettings.Development.json", optional: false);

            return builder.Build();
        }
    }
}
