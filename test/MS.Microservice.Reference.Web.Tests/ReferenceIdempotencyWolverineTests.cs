using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MS.Microservice.Idempotency.EFCore;
using MS.Microservice.Messaging.Wolverine;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Persistence;
using NSubstitute;
using Wolverine.EntityFrameworkCore;
using Xunit;

namespace MS.Microservice.Reference.Web.Tests;

public sealed class ReferenceIdempotencyWolverineTests
{
    [Fact]
    public async Task NestedProfileServiceAndResponseJoinWolverineUnitOfWorkTransaction()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SelfManagedReferenceDbContext>().UseSqlite(connection).Options;
        await using (var context = new SelfManagedReferenceDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
            var native = Substitute.For<IDbContextOutbox<SelfManagedReferenceDbContext>>();
            native.SaveChangesAndFlushMessagesAsync(Arg.Any<CancellationToken>()).Returns(async call =>
            {
                await context.SaveChangesAsync(call.Arg<CancellationToken>());
                await context.Database.CommitTransactionAsync(call.Arg<CancellationToken>());
            });
            var unit = new WolverineUnitOfWork<SelfManagedReferenceDbContext>(context,
                () => native, ReferenceMessages.Topology().Registry);
            var service = new ProfileService(new ProfileRepository(context), unit, unit, TimeProvider.System);
            var store = new EfCoreIdempotencyStore<SelfManagedReferenceDbContext>(context, TimeProvider.System);
            var request = IdempotencyRequest.Create("profiles.create", "actor", "key", "create-profile"u8);

            var response = await unit.ExecuteAsync(token => store.ClaimAndExecuteAsync(request, TimeSpan.FromHours(24),
                async operationToken =>
                {
                    var created = await service.CreateAsync(
                        new("https://issuer.example", "subject", "first", ["reader"]),
                        new("https://issuer.example", "admin"), operationToken);
                    Assert.True(created.IsRight);
                    return new IdempotencyResponse(201, "application/json", "{}"u8);
                }, token));

            Assert.Equal(201, response.StatusCode);
            await native.Received(1).SaveChangesAndFlushMessagesAsync(Arg.Any<CancellationToken>());
        }

        await using var check = new SelfManagedReferenceDbContext(options);
        Assert.Equal(1, await check.Profiles.CountAsync());
        var replay = await new EfCoreIdempotencyStore<SelfManagedReferenceDbContext>(check, TimeProvider.System)
            .FindAsync(IdempotencyRequest.Create("profiles.create", "actor", "key", "create-profile"u8));
        Assert.Equal(IdempotencyLookupKind.Replay, replay.Kind);
    }
}
