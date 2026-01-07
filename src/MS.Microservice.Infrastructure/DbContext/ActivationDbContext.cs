using MS.Microservice.Core.Domain.Entity;
using MS.Microservice.Core.Domain.Repository;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Infrastructure.EntityConfigurations;
using Wolverine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Action = MS.Microservice.Domain.Aggregates.IdentityModel.Action;
using EfCoreDbContext = Microsoft.EntityFrameworkCore.DbContext;
using MS.Microservice.Domain.Aggregates.LogAggregate;
using System.Collections.Generic;

namespace MS.Microservice.Infrastructure.DbContext
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
        //public DbSet<MerchandiseSubject> MerchandiseSubjects { get; set; }
        private readonly IMessageBus _messageBus;
        private readonly MsPlatformDbContextSettings _platformDbContextOption;

        public ActivationDbContext(
            DbContextOptions<ActivationDbContext> options,
            IConfiguration configuration,
            IMessageBus messageBus) : base(options)
        {
            _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
            _platformDbContextOption = configuration.GetSection("FzPlatformDbContextSettings").Get<MsPlatformDbContextSettings>()!;
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
            //modelBuilder.ApplyConfiguration(new UseActivationDeviceEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new IdentityUserEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new IdentityRoleEntityTypeConfiguration());
            modelBuilder.ApplyConfiguration(new IdentityActionEntityTypeConfiguration());
            //modelBuilder.ApplyConfigurationsFromAssembly(typeof(ActivateBatchEntityTypeConfiguration).Assembly);

            SettingDatetimePrecision(modelBuilder);
        }

        private static void SettingDatetimePrecision(ModelBuilder modelBuilder)
        {
            var properties = modelBuilder.Model.GetEntityTypes()
                .SelectMany(t => t.GetProperties());

            foreach (var property in properties
                .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)))
            {
                // EF Core 5
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
        }        public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
        {
            await _messageBus.DispatchDomainEventsAsync(this);
            await SaveChangesAsync(cancellationToken);
            return true;
        }

        #region 事务
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
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
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
        #endregion
    }

    public class ActivationDbContextDesignFactory : IDesignTimeDbContextFactory<ActivationDbContext>
    {        public ActivationDbContext CreateDbContext(string[] args)
        {
            var configuration = BuildConfiguration();
            var connectionString = configuration.GetConnectionString("ActivationConnection");
            var builder = new DbContextOptionsBuilder<ActivationDbContext>()
                //.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
                .UseNpgsql(connectionString);

            return new ActivationDbContext(builder.Options, configuration, new NoMessageBus());
        }

        private static IConfigurationRoot BuildConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../Migrators/"))
                .AddJsonFile("appsettings.Development.json", optional: false);

            return builder.Build();
        }

        /// <summary>
        /// A no-op implementation of IMessageBus for design-time DbContext creation
        /// </summary>
        class NoMessageBus : IMessageBus
        {
            public string? CorrelationId { get; set; }
            public string? TenantId { get; set; }

            public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
            {
                return ValueTask.CompletedTask;
            }

            public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null)
            {
                return ValueTask.CompletedTask;
            }

            public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
            {
                return Task.FromResult<T>(default!);
            }

            public Task<T> InvokeAsync<T>(object message, DeliveryOptions options, CancellationToken cancellation = default, TimeSpan? timeout = null)
            {
                return Task.FromResult<T>(default!);
            }

            public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
            {
                return Task.CompletedTask;
            }

            public Task InvokeAsync(object message, DeliveryOptions options, CancellationToken cancellation = default, TimeSpan? timeout = null)
            {
                return Task.CompletedTask;
            }            public Task<T> InvokeForTenantAsync<T>(string tenantId, object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
            {
                return Task.FromResult<T>(default!);
            }

            public Task InvokeForTenantAsync(string tenantId, object message, CancellationToken cancellation = default, TimeSpan? timeout = null)
            {
                return Task.CompletedTask;
            }

            public IDestinationEndpoint EndpointFor(string endpointName)
            {
                return null!;
            }

            public IDestinationEndpoint EndpointFor(Uri uri)
            {
                return null!;
            }

            public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions? options = null)
            {
                return Array.Empty<Envelope>();
            }

            public IReadOnlyList<Envelope> PreviewSubscriptions(object message)
            {
                return Array.Empty<Envelope>();
            }
        }
    }
}
