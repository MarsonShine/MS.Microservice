using MS.Microservice.Core.Domain.Entity;
using MS.Microservice.Core.Domain.Repository;
using MS.Microservice.Domain.Aggregates.IdentityModel;
using MS.Microservice.Infrastructure.EntityConfigurations;
using MediatR;
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

namespace MS.Microservice.Infrastructure.DbContext
{
    public class ActivationDbContext : EfCoreDbContext, IUnitOfWork
    {
        public const string DEFAULT_SCHEMA = "fz_platform_activation";
        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Action> Actions { get; set; }
        public DbSet<RoleAction> RoleActions { get; set; }
        public DbSet<LogAggregateRoot> Logs { get; set; }
        //public DbSet<MerchandiseSubject> MerchandiseSubjects { get; set; }
        private readonly IMediator _mediator;
        private readonly MsPlatformDbContextSettings _platformDbContextOption;

        public ActivationDbContext(
            DbContextOptions<ActivationDbContext> options,
            IConfiguration configuration,
            IMediator mediator) : base(options)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _platformDbContextOption = configuration.GetSection("FzPlatformDbContextSettings").Get<MsPlatformDbContextSettings>();
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
        }

        public async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
        {
            await _mediator.DispatchDomainEventsAsync(this);
            await SaveChangesAsync(cancellationToken);
            return true;
        }

        #region 事务
        private IDbContextTransaction _currentTransaction;
        public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
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
    {
        public ActivationDbContext CreateDbContext(string[] args)
        {
            var configuration = BuildConfiguration();
            var connectionString = configuration.GetConnectionString("ActivationConnection");
            var builder = new DbContextOptionsBuilder<ActivationDbContext>()
                .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));

            return new ActivationDbContext(builder.Options, configuration, new NoMediator());
        }

        private static IConfigurationRoot BuildConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../Migrators/"))
                .AddJsonFile("appsettings.Development.json", optional: false);

            return builder.Build();
        }

        class NoMediator : IMediator
        {
            public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
            {
                return Task.CompletedTask;
            }

            public Task Publish(object notification, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<TResponse>(default!);
            }

            public Task<object> Send(object request, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(default(object));
            }
        }
    }
}
