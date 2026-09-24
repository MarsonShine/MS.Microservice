using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NSubstitute;
using RabbitMQ.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MS.Microservice.AspNetCore;
using MS.Microservice.Idempotency.EFCore;
using MS.Microservice.Messaging;
using MS.Microservice.Messaging.RabbitMQ;
using MS.Microservice.Messaging.SelfManaged;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Domain;
using MS.Microservice.Reference.Persistence;
using Xunit;
using MS.Microservice.Reference.Web.HttpIdempotency;

namespace MS.Microservice.Reference.Web.Tests;

public sealed class ReferenceHostTests
{
    [Fact]
    public async Task FormalHostRequiresIdentityAndContainsNoLabOrLocalLoginRoutes()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var live = await fixture.Client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("{\"status\":\"healthy\"}", await live.Content.ReadAsStringAsync());
        using var anonymous = await fixture.Client.GetAsync("/api/v1/profiles");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var anonymousOrder = await fixture.Client.PostAsJsonAsync("/api/v1/orders", new CreateOrder("SKU-1", 1));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousOrder.StatusCode);
        fixture.Authenticate("profiles.manage");
        foreach (var path in new[] { "/api/demo", "/api/v1/Account/login", "/api/orders", "/api/image" })
        {
            using var response = await fixture.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task ProfileCrudAndOperationsPermissionsUseTheActualHostPipeline()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Authenticate("profiles.manage");
        var request = new CreateProfile("https://issuer.example", "external-subject", "first", ["reader"]);
        using var created = await fixture.Client.PostAsJsonAsync("/api/v1/profiles", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var profile = (await created.Content.ReadFromJsonAsync<ProfileView>())!;
        using var changed = await fixture.Client.PatchAsJsonAsync($"/api/v1/profiles/{profile.Id}", new ChangeProfile("second", ["editor"], 1));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(2, (await changed.Content.ReadFromJsonAsync<ProfileView>())!.Version);
        using var stale = await fixture.Client.PatchAsJsonAsync($"/api/v1/profiles/{profile.Id}", new ChangeProfile("third", [], 1));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await AssertProblemCodeAsync(stale, "conflict");
        using var duplicate = await fixture.Client.PostAsJsonAsync("/api/v1/profiles", request);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await AssertProblemCodeAsync(duplicate, "conflict");
        using var denied = await fixture.Client.GetAsync("/api/operations/messages/failures");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        fixture.Authenticate("messaging.manage");
        using var allowed = await fixture.Client.GetAsync("/api/operations/messages/failures");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledIdempotencyIgnoresKeysWithoutRequiringItsTable(bool explicitDisable)
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: explicitDisable ? false : null);
        fixture.Authenticate("profiles.manage");
        await fixture.ExecuteSqlAsync("DROP TABLE HttpIdempotency");
        var request = new CreateProfile("https://issuer.example", "disabled-one", "first", []);

        using var first = await PostKeyedAsync(fixture.Client, request, "same-key");
        using var duplicate = await PostKeyedAsync(fixture.Client, request, "same-key");
        using var invalid = await PostKeyedAsync(fixture.Client,
            request with { Subject = "disabled-two" }, "bad,key");
        using var multiple = await PostKeyedAsync(fixture.Client,
            request with { Subject = "disabled-three" }, "one", "two");
        using var unkeyed = await fixture.Client.PostAsJsonAsync("/api/v1/profiles",
            request with { Subject = "disabled-four" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Created, multiple.StatusCode);
        Assert.Equal(HttpStatusCode.Created, unkeyed.StatusCode);
        Assert.Equal(4, await fixture.CountProfilesAsync());
        Assert.False(await fixture.HasIdempotencyTableAsync());
    }

    [Theory]
    [InlineData("SelfManaged", false)]
    [InlineData("SelfManaged", true)]
    [InlineData("Wolverine", false)]
    [InlineData("Wolverine", true)]
    public void IdempotencyServicesAreRegisteredOnlyWhenEnabled(string provider, bool enabled)
    {
        var builder = Fixture.Builder(provider);
        builder.Configuration["Http:Idempotency:Enabled"] = enabled.ToString();
        ReferenceHost.AddServices(builder);

        Assert.Equal(enabled, builder.Services.Any(descriptor =>
            descriptor.ServiceType == typeof(EfCoreIdempotencyStore<ReferenceDbContext>)));
        Assert.Equal(enabled, builder.Services.Any(descriptor =>
            descriptor.ServiceType.Name == "ReferenceHttpIdempotencyExecutor"));
        Assert.Equal(enabled, builder.Services.Any(descriptor =>
            descriptor.ServiceType.Name == "IIdempotencyActorScope"));
        Assert.Equal(enabled, builder.Services.Any(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType?.Name == "ReferenceIdempotencyCleanupWorker"));
    }

    [Fact]
    public void InvalidIdempotencyConfigurationFailsDuringComposition()
    {
        var builder = Fixture.Builder();
        builder.Configuration["Http:Idempotency:Enabled"] = "sometimes";
        var exception = Assert.Throws<ArgumentException>(() => ReferenceHost.AddServices(builder));
        Assert.Contains("Http:Idempotency:Enabled", exception.Message);
    }

    [Fact]
    public async Task KeyedProfileCreationReplaysExactBytesAfterReorderingJson()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        fixture.Authenticate("profiles.manage");
        var request = new CreateProfile("https://issuer.example", "keyed-subject", "first", ["reader"]);

        using var created = await PostKeyedAsync(fixture.Client, request, "create-123");
        var firstBody = await created.Content.ReadAsByteArrayAsync();
        using var replay = await PostKeyedAsync(fixture.Client, request, "create-123");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(created.StatusCode, replay.StatusCode);
        Assert.Equal(created.Headers.Location, replay.Headers.Location);
        Assert.Equal(created.Content.Headers.ContentType, replay.Content.Headers.ContentType);
        Assert.Equal(firstBody, await replay.Content.ReadAsByteArrayAsync());

        using var changedHeader = new HttpRequestMessage(HttpMethod.Post, "/api/v1/profiles")
        {
            Content = JsonContent.Create(request)
        };
        changedHeader.Headers.TryAddWithoutValidation("X-Request-Id", "another-attempt");
        changedHeader.Headers.TryAddWithoutValidation("Idempotency-Key", "create-123");
        using var headerReplay = await fixture.Client.SendAsync(changedHeader);
        Assert.Equal(HttpStatusCode.Created, headerReplay.StatusCode);
        Assert.Equal(firstBody, await headerReplay.Content.ReadAsByteArrayAsync());

        using var reordered = new HttpRequestMessage(HttpMethod.Post, "/api/v1/profiles")
        {
            Content = new StringContent("""
                {"roles":["reader"],"displayName":"first","subject":"keyed-subject","issuer":"https://issuer.example"}
                """, Encoding.UTF8, "application/json")
        };
        reordered.Headers.TryAddWithoutValidation("Idempotency-Key", "create-123");
        using var reorderedReplay = await fixture.Client.SendAsync(reordered);
        Assert.Equal(HttpStatusCode.Created, reorderedReplay.StatusCode);
        Assert.Equal(created.Headers.Location, reorderedReplay.Headers.Location);
        Assert.Equal(firstBody, await reorderedReplay.Content.ReadAsByteArrayAsync());
        Assert.Equal((1, 1, 1), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task NestedJsonObjectsIgnorePropertyOrderButArraysKeepTheirOrder()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");

        using var first = await PostRawKeyedAsync("""
            {"items":[{"a":1,"b":2},{"a":3,"b":4}],"label":"one"}
            """);
        using var reordered = await PostRawKeyedAsync("""
            { "label": "one", "items": [ {"b":2,"a":1}, {"b":4,"a":3} ] }
            """);
        using var changedArray = await PostRawKeyedAsync("""
            {"label":"one","items":[{"b":4,"a":3},{"b":2,"a":1}]}
            """);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reordered.StatusCode);
        Assert.Equal(await first.Content.ReadAsByteArrayAsync(), await reordered.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.Conflict, changedArray.StatusCode);
        Assert.Equal((0, 0, 1), await fixture.CountWritesAsync());

        async Task<HttpResponseMessage> PostRawKeyedAsync(string json)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/test/mvc/profiles/json")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", "nested-json-key");
            return await fixture.Client.SendAsync(request);
        }
    }

    [Fact]
    public async Task KeyedMvcJsonAcceptsUtf16JustLikeTheUnkeyedAction()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");

        using var unkeyed = await PostUtf16Async("{\"a\":1,\"b\":2}", null);
        using var first = await PostUtf16Async("{\"a\":1,\"b\":2}", "utf16-key");
        using var reordered = await PostUtf16Async("{\"b\":2,\"a\":1}", "utf16-key");

        Assert.Equal(HttpStatusCode.OK, unkeyed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reordered.StatusCode);
        Assert.Equal(await first.Content.ReadAsByteArrayAsync(), await reordered.Content.ReadAsByteArrayAsync());
        Assert.Equal((0, 0, 1), await fixture.CountWritesAsync());

        async Task<HttpResponseMessage> PostUtf16Async(string json, string? key)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/test/mvc/profiles/json")
            {
                Content = new StringContent(json, Encoding.Unicode, "application/json")
            };
            if (key is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
            return await fixture.Client.SendAsync(request);
        }
    }

    [Fact]
    public async Task DuplicateJsonPropertyIsRejectedBeforeCreatingAClaim()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");
        foreach (var json in new[]
        {
            "{\"name\":1,\"name\":2}",
            "{\"displayName\":\"A\",\"DisplayName\":\"B\"}",
            "{\"DisplayName\":\"B\",\"displayName\":\"A\"}"
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/test/mvc/profiles/json")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", "duplicate-property-key");

            using var response = await fixture.Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            await AssertProblemCodeAsync(response, "validation");
        }
        Assert.Equal((0, 0, 0), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task OldProfileFingerprintIsRejectedByTheCurrentComparisonRule()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");
        var profile = new CreateProfile("https://issuer.example", "old-claim", "first", ["reader"]);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var actorBytes = JsonSerializer.SerializeToUtf8Bytes(
            new AuditActor("https://issuer.example", "test-admin"), options);
        var actorHash = Convert.ToHexString(SHA256.HashData(actorBytes));
        var key = "legacy-key";
        var legacyResponse = Encoding.UTF8.GetBytes("{\"legacy\":true}");
        await fixture.SeedIdempotencyAsync(new IdempotencyRecord
        {
            ScopeHash = Hash($"profiles.create\0{actorHash}"),
            KeyHash = Hash(key),
            RequestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(profile, options))),
            ExpiresAtUtcTicks = DateTime.UtcNow.AddHours(1).Ticks,
            CompletedAtUtcTicks = DateTime.UtcNow.Ticks,
            StatusCode = 201,
            ContentType = "application/json; charset=utf-8",
            Location = "/api/v1/profiles/old",
            Body = legacyResponse
        });

        using var replay = await PostKeyedAsync(fixture.Client, profile, key);
        using var utf16Request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/profiles")
        {
            Content = new StringContent(JsonSerializer.Serialize(profile, options), Encoding.Unicode,
                "application/json")
        };
        utf16Request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        using var utf16Replay = await fixture.Client.SendAsync(utf16Request);
        using var otherRoute = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles", profile, key);
        using var changed = await PostKeyedAsync(fixture.Client,
            profile with { DisplayName = "second" }, key);

        foreach (var response in new[] { replay, utf16Replay, otherRoute, changed })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            await AssertProblemCodeAsync(response, "conflict");
        }
        Assert.Equal((0, 0, 1), await fixture.CountWritesAsync());

        static string Hash(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    [Fact]
    public async Task KeyedOrderCreationReplaysAfterJsonReorderingWithoutAnotherWrite()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        fixture.Authenticate("unrelated.scope");
        var order = new CreateOrder(" SKU-42 ", 2);

        using var created = await PostKeyedOrderAsync(fixture.Client, order, "order-key");
        using var reorderedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = new StringContent("{\"quantity\":2,\"sku\":\" SKU-42 \"}", Encoding.UTF8,
                "application/json")
        };
        reorderedRequest.Headers.TryAddWithoutValidation("Idempotency-Key", "order-key");
        using var replay = await fixture.Client.SendAsync(reorderedRequest);
        var createdOrder = (await created.Content.ReadFromJsonAsync<OrderView>())!;
        using var fetched = await fixture.Client.GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(created.StatusCode, replay.StatusCode);
        Assert.Equal(created.Headers.Location, replay.Headers.Location);
        Assert.Equal(await created.Content.ReadAsByteArrayAsync(), await replay.Content.ReadAsByteArrayAsync());
        Assert.Equal("SKU-42", createdOrder.Sku);
        Assert.Equal(2, createdOrder.Quantity);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(createdOrder, await fetched.Content.ReadFromJsonAsync<OrderView>());
        Assert.Equal(1, await fixture.CountOrdersAsync());
        Assert.Equal((0, 0, 1), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task AnotherAuthenticatedActorCannotReadTheOrder()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        fixture.Authenticate("unrelated.scope", "order-owner");
        using var created = await PostKeyedOrderAsync(fixture.Client, new CreateOrder("SKU-42", 2), "owner-key");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        fixture.Authenticate("unrelated.scope", "another-actor");
        using var hidden = await fixture.Client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        fixture.Authenticate("unrelated.scope", "order-owner");
        using var visible = await fixture.Client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        Assert.Equal(1, await fixture.CountOrdersAsync());
    }

    [Fact]
    public async Task OrderKeyRejectsChangedBodyButDifferentKeyCreatesAnotherOrder()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        fixture.Authenticate("unrelated.scope");
        var order = new CreateOrder("SKU-42", 2);

        using var first = await PostKeyedOrderAsync(fixture.Client, order, "first-order-key");
        using var changed = await PostKeyedOrderAsync(fixture.Client, order with { Quantity = 3 }, "first-order-key");
        using var separate = await PostKeyedOrderAsync(fixture.Client, order, "second-order-key");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        await AssertProblemCodeAsync(changed, "conflict");
        Assert.Equal(HttpStatusCode.Created, separate.StatusCode);
        Assert.NotEqual(first.Headers.Location, separate.Headers.Location);
        Assert.Equal(2, await fixture.CountOrdersAsync());
        Assert.Equal((0, 0, 2), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task InvalidOrderCanReuseItsKeyAfterCorrection()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        fixture.Authenticate("unrelated.scope");

        using var badQuantity = await PostKeyedOrderAsync(fixture.Client, new CreateOrder("SKU-42", 0), "fix-order-key");
        using var badSku = await PostKeyedOrderAsync(fixture.Client, new CreateOrder(" ", 1), "fix-order-key");
        using var created = await PostKeyedOrderAsync(fixture.Client, new CreateOrder("SKU-42", 2), "fix-order-key");

        Assert.Equal(HttpStatusCode.BadRequest, badQuantity.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badSku.StatusCode);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(1, await fixture.CountOrdersAsync());
        Assert.Equal((0, 0, 1), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task OrderCreationWithoutActiveIdempotencyDoesNotRequireTheTable()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: false);
        fixture.Authenticate("unrelated.scope");
        await fixture.ExecuteSqlAsync("DROP TABLE HttpIdempotency");

        using var keyed = await PostKeyedOrderAsync(fixture.Client, new CreateOrder("SKU-1", 1), "bad,key");
        using var unkeyed = await fixture.Client.PostAsJsonAsync("/api/v1/orders", new CreateOrder("SKU-2", 2));

        Assert.Equal(HttpStatusCode.Created, keyed.StatusCode);
        Assert.Equal(HttpStatusCode.Created, unkeyed.StatusCode);
        Assert.Equal(2, await fixture.CountOrdersAsync());
        Assert.False(await fixture.HasIdempotencyTableAsync());
    }

    [Fact]
    public async Task EnabledIdempotencyDoesNotReadTheTableForAnUnkeyedOrder()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        fixture.Authenticate("unrelated.scope");
        await fixture.ExecuteSqlAsync("DROP TABLE HttpIdempotency");

        using var created = await fixture.Client.PostAsJsonAsync("/api/v1/orders", new CreateOrder("SKU-3", 3));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(1, await fixture.CountOrdersAsync());
        Assert.False(await fixture.HasIdempotencyTableAsync());
    }

    [Fact]
    public async Task ProfileAndOrderCanUseTheSameKeyUnderDifferentOperations()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        fixture.Authenticate("profiles.manage");

        using var profile = await PostKeyedAsync(fixture.Client,
            new CreateProfile("https://issuer.example", "separate-operation", "first", []), "shared-key");
        using var order = await PostKeyedOrderAsync(fixture.Client, new CreateOrder("SKU-4", 4), "shared-key");

        Assert.Equal(HttpStatusCode.Created, profile.StatusCode);
        Assert.Equal(HttpStatusCode.Created, order.StatusCode);
        Assert.Equal(1, await fixture.CountOrdersAsync());
        Assert.Equal((1, 1, 2), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task EnabledIdempotencyLeavesUnkeyedRequestsOnTheOriginalPath()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        fixture.Authenticate("profiles.manage");
        var request = new CreateProfile("https://issuer.example", "unkeyed-subject", "first", []);

        using var first = await fixture.Client.PostAsJsonAsync("/api/v1/profiles", request);
        using var duplicate = await fixture.Client.PostAsJsonAsync("/api/v1/profiles", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal((1, 1, 0), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task GloballyEnabledIdempotencyDoesNotChangeUnmarkedPostEndpoints()
    {
        var failures = Substitute.For<IFailedMessageOperations>();
        failures.ReplayAsync("missing", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ReplayResult.NotFound));
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true,
            configureServices: services =>
            {
                services.RemoveAll<IFailedMessageOperations>();
                services.AddSingleton(failures);
            });
        fixture.Authenticate("messaging.manage");
        await fixture.ExecuteSqlAsync("DROP TABLE HttpIdempotency");

        foreach (var keys in new[]
        {
            Array.Empty<string>(), ["safe-key"], ["bad,key"], ["one", "two"], [new string('x', 129)]
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "/api/operations/messages/failures/missing/replay");
            if (keys.Length != 0)
                Assert.True(request.Headers.TryAddWithoutValidation("Idempotency-Key", keys));
            using var response = await fixture.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        await failures.Received(5).ReplayAsync("missing", Arg.Any<CancellationToken>());
        Assert.False(await fixture.HasIdempotencyTableAsync());
    }

    [Fact]
    public async Task MarkedMvcActionReplaysTheCreatedResponseWithoutAnotherWrite()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");
        var request = new CreateProfile("https://issuer.example", "mvc-subject", "first", ["reader"]);

        using var first = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles", request, "mvc-key");
        using var replay = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles", request, "mvc-key");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(first.StatusCode, replay.StatusCode);
        Assert.Equal(first.Headers.Location, replay.Headers.Location);
        Assert.Equal(await first.Content.ReadAsByteArrayAsync(), await replay.Content.ReadAsByteArrayAsync());
        Assert.Equal((1, 1, 1), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task MvcActionsShareOneFilterButHaveSeparateOperationScopes()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");
        var request = new CreateProfile("https://issuer.example", "mvc-first", "first", []);

        using var first = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles", request, "shared-key");
        using var second = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles/alternate",
            request with { Subject = "mvc-second" }, "shared-key");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal((2, 2, 2), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task UnrelatedMvcApiNeedsOnlyTheOperationMarker()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");

        using var first = await PostAsync("{\"left\":1,\"right\":2}");
        using var reordered = await PostAsync("{\"right\":2,\"left\":1}");
        using var changed = await PostAsync("{\"left\":1,\"right\":3}");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reordered.StatusCode);
        Assert.Equal(await first.Content.ReadAsByteArrayAsync(), await reordered.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal((0, 0, 1), await fixture.CountWritesAsync());

        async Task<HttpResponseMessage> PostAsync(string json)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/test/mvc/echo")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", "echo-key");
            return await fixture.Client.SendAsync(request);
        }
    }

    [Fact]
    public async Task SharedMvcFilterReplaysOkResponseWithoutInventingLocation()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");
        var request = new CreateProfile("https://issuer.example", "mvc-echo", "echo", []);

        using var first = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles/echo", request, "echo-key");
        using var replay = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles/echo", request, "echo-key");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(first.StatusCode, replay.StatusCode);
        Assert.Null(first.Headers.Location);
        Assert.Null(replay.Headers.Location);
        Assert.Equal(await first.Content.ReadAsByteArrayAsync(), await replay.Content.ReadAsByteArrayAsync());
        Assert.Equal((0, 0, 1), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task SharedMvcFilterReplaysNoContentResponse()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");
        var request = new CreateProfile("https://issuer.example", "mvc-empty", "empty", []);

        using var first = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles/empty", request, "empty-key");
        using var replay = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles/empty", request, "empty-key");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(first.StatusCode, replay.StatusCode);
        Assert.Null(replay.Content.Headers.ContentType);
        Assert.Empty(await replay.Content.ReadAsByteArrayAsync());
        Assert.Equal((0, 0, 1), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task OversizedResponseRollsBackTheIdempotencyClaim()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");

        using var response = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles/echo",
            new CreateProfile("https://issuer.example", "mvc-large-response", new string('x', 70_000), []),
            "large-response-key");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal((0, 0, 0), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task OversizedKeyedRequestIsRejectedBeforeTheAction()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");

        using var response = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles/echo",
            new CreateProfile("https://issuer.example", "mvc-large-request", new string('x', 1_048_576), []),
            "large-request-key");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal((0, 0, 0), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task SameKeyAndBodyWithDifferentQueryIsAConflict()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");
        var request = new CreateProfile("https://issuer.example", "mvc-query", "first", []);

        using var first = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles?mode=first",
            request, "query-key");
        using var differentQuery = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles?mode=second",
            request, "query-key");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, differentQuery.StatusCode);
        Assert.Equal((1, 1, 1), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task UnmarkedMvcActionIgnoresKeysWhenInfrastructureIsEnabled()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");
        await fixture.ExecuteSqlAsync("DROP TABLE HttpIdempotency");
        var request = new CreateProfile("https://issuer.example", "mvc-plain-one", "first", []);

        using var invalid = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles/plain", request, "bad,key");
        using var multiple = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles/plain",
            request with { Subject = "mvc-plain-two" }, "one", "two");
        using var valid = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles/plain",
            request with { Subject = "mvc-plain-three" }, "safe-key");

        Assert.Equal(HttpStatusCode.Created, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Created, multiple.StatusCode);
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        Assert.Equal(3, await fixture.CountProfilesAsync());
        Assert.False(await fixture.HasIdempotencyTableAsync());
    }

    [Fact]
    public async Task MarkedMvcActionUsesThePlainPathWhenInfrastructureIsDisabled()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: false, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");
        await fixture.ExecuteSqlAsync("DROP TABLE HttpIdempotency");
        var request = new CreateProfile("https://issuer.example", "mvc-disabled", "first", []);

        using var first = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles", request, "bad,key");
        using var duplicate = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles", request, "bad,key");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(1, await fixture.CountProfilesAsync());
        Assert.False(await fixture.HasIdempotencyTableAsync());
    }

    [Fact]
    public async Task MarkedMvcActionWithoutKeyDoesNotUseIdempotencyStorage()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");
        await fixture.ExecuteSqlAsync("DROP TABLE HttpIdempotency");
        var request = new CreateProfile("https://issuer.example", "mvc-unkeyed", "first", []);

        using var unkeyed = await fixture.Client.PostAsJsonAsync("/test/mvc/profiles", request);
        using var invalid = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles",
            request with { Subject = "mvc-invalid" }, "bad,key");
        using var multiple = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles",
            request with { Subject = "mvc-multiple" }, "one", "two");

        Assert.Equal(HttpStatusCode.Created, unkeyed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, multiple.StatusCode);
        Assert.Equal(1, await fixture.CountProfilesAsync());
        Assert.False(await fixture.HasIdempotencyTableAsync());
    }

    [Fact]
    public async Task MarkedMvcActionRollsBackRejectedKeyAndRejectsChangedRequest()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");
        var invalidRequest = new CreateProfile("https://issuer.example", "mvc-reusable", "first", ["invalid"]);

        using var rejected = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles", invalidRequest, "mvc-key");
        using var created = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles",
            invalidRequest with { Roles = ["reader"] }, "mvc-key");
        using var changed = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles",
            invalidRequest with { Roles = ["reader"], DisplayName = "changed" }, "mvc-key");

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal((1, 1, 1), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task MvcBusinessConflictHasTheSamePublicErrorWithAndWithoutKey()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true);
        fixture.Authenticate("profiles.manage");
        var request = new CreateProfile("https://issuer.example", "mvc-conflict", "first", []);
        using var created = await fixture.Client.PostAsJsonAsync("/test/mvc/profiles", request);

        using var unkeyed = await fixture.Client.PostAsJsonAsync("/test/mvc/profiles", request);
        using var keyed = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles", request, "mvc-conflict-key");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, unkeyed.StatusCode);
        Assert.Equal(unkeyed.StatusCode, keyed.StatusCode);
        Assert.Equal(unkeyed.Content.Headers.ContentType, keyed.Content.Headers.ContentType);
        using var unkeyedBody = JsonDocument.Parse(await unkeyed.Content.ReadAsStringAsync());
        using var keyedBody = JsonDocument.Parse(await keyed.Content.ReadAsStringAsync());
        Assert.Equal("conflict", unkeyedBody.RootElement.GetProperty("code").GetString());
        Assert.Equal(unkeyedBody.RootElement.GetProperty("code").GetString(),
            keyedBody.RootElement.GetProperty("code").GetString());
        Assert.Equal(unkeyedBody.RootElement.GetProperty("title").GetString(),
            keyedBody.RootElement.GetProperty("title").GetString());
        Assert.Equal((1, 1, 0), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task MarkedMvcActionRollsBackOnServerFailure()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, mvcEndpoints: true,
            configureServices: services =>
            {
                services.RemoveAll<IIntegrationEventPublisher>();
                services.AddSingleton<IIntegrationEventPublisher>(new FailingPublisher(new IOException("publisher failed")));
            });
        fixture.Authenticate("profiles.manage");

        using var response = await PostKeyedToAsync(fixture.Client, "/test/mvc/profiles",
            new CreateProfile("https://issuer.example", "mvc-failure", "first", []), "mvc-key");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal((0, 0, 0), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task KeyedRequestWithDifferentPayloadReturnsConflictWithoutReplayingPrivateResponse()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        fixture.Authenticate("profiles.manage");
        var request = new CreateProfile("https://issuer.example", "keyed-subject", "first", ["reader"]);

        using var created = await PostKeyedAsync(fixture.Client, request, "create-123");
        using var conflict = await PostKeyedAsync(fixture.Client, request with { DisplayName = "changed" }, "create-123");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var body = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        Assert.Equal("conflict", body.RootElement.GetProperty("code").GetString());
        Assert.Equal((1, 1, 1), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task SameKeyBelongsToTheAuthenticatedActor()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        var firstRequest = new CreateProfile("https://issuer.example", "first-subject", "first", []);
        fixture.Authenticate("profiles.manage", "first-admin");
        using var first = await PostKeyedAsync(fixture.Client, firstRequest, "shared-key");
        fixture.Authenticate("profiles.manage", "second-admin");
        using var second = await PostKeyedAsync(fixture.Client,
            firstRequest with { Subject = "second-subject" }, "shared-key");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal((2, 2, 2), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task RefreshedTokenReplaysForTheSameActorButLosingPermissionBlocksReplay()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        var request = new CreateProfile("https://issuer.example", "refreshed-token", "first", []);
        fixture.Authenticate("profiles.manage", tokenId: "first-token");
        using var first = await PostKeyedAsync(fixture.Client, request, "refresh-key");

        fixture.Authenticate("messaging.manage", tokenId: "denied-token");
        using var denied = await PostKeyedAsync(fixture.Client, request, "refresh-key");

        fixture.Authenticate("profiles.manage", tokenId: "second-token");
        using var replay = await PostKeyedAsync(fixture.Client, request, "refresh-key");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsByteArrayAsync(), await replay.Content.ReadAsByteArrayAsync());
        Assert.Equal((1, 1, 1), await fixture.CountWritesAsync());
    }

    [Theory]
    [InlineData("bad,key")]
    [InlineData("has space")]
    public async Task InvalidIdempotencyKeyIsRejectedBeforeBusinessWrite(string key)
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        fixture.Authenticate("profiles.manage");
        var request = new CreateProfile("https://issuer.example", "keyed-subject", "first", []);

        using var invalid = await PostKeyedAsync(fixture.Client, request, key);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal((0, 0, 0), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task MultipleAndOverlongIdempotencyKeysAreRejected()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        fixture.Authenticate("profiles.manage");
        var request = new CreateProfile("https://issuer.example", "keyed-subject", "first", []);

        using var multiple = await PostKeyedAsync(fixture.Client, request, "one", "two");
        using var overlong = await PostKeyedAsync(fixture.Client, request, new string('x', 129));

        Assert.Equal(HttpStatusCode.BadRequest, multiple.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, overlong.StatusCode);
        Assert.Equal((0, 0, 0), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task BusinessValidationAndConflictDoNotReserveTheKey()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        fixture.Authenticate("profiles.manage");
        var request = new CreateProfile("https://issuer.example", "keyed-subject", "first", ["invalid"]);

        using var invalid = await PostKeyedAsync(fixture.Client, request, "reusable-key");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal((0, 0, 0), await fixture.CountWritesAsync());

        using var created = await PostKeyedAsync(fixture.Client, request with { Roles = ["reader"] }, "reusable-key");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var duplicate = await PostKeyedAsync(fixture.Client, request with { Roles = ["reader"] }, "new-key");
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal((1, 1, 1), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task PruningAnExpiredSnapshotRemovesOnlyTheIdempotencyRecord()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        fixture.Authenticate("profiles.manage");
        using var created = await PostKeyedAsync(fixture.Client,
            new CreateProfile("https://issuer.example", "keyed-subject", "first", []), "create-123");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        await fixture.ExecuteSqlAsync("UPDATE HttpIdempotency SET ExpiresAtUtcTicks = 0");
        Assert.Equal(1, await fixture.PruneExpiredAsync());
        Assert.Equal((1, 1, 0), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task MissingIdempotencyTableDoesNotChangeUnkeyedWritesOrCreateSchema()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true);
        fixture.Authenticate("profiles.manage");
        await fixture.ExecuteSqlAsync("DROP TABLE HttpIdempotency");

        using var unkeyed = await fixture.Client.PostAsJsonAsync("/api/v1/profiles",
            new CreateProfile("https://issuer.example", "unkeyed-subject", "first", []));
        using var keyed = await PostKeyedAsync(fixture.Client,
            new CreateProfile("https://issuer.example", "keyed-subject", "second", []), "create-123");

        Assert.Equal(HttpStatusCode.Created, unkeyed.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, keyed.StatusCode);
        Assert.Equal(1, await fixture.CountProfilesAsync());
        Assert.False(await fixture.HasIdempotencyTableAsync());
    }

    [Fact]
    public async Task ServerFailureRollsBackProfileOutboxAndIdempotencyClaim()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, configureServices: services =>
        {
            services.RemoveAll<IIntegrationEventPublisher>();
            services.AddSingleton<IIntegrationEventPublisher>(new FailingPublisher(new IOException("publisher failed")));
        });
        fixture.Authenticate("profiles.manage");

        using var response = await PostKeyedAsync(fixture.Client,
            new CreateProfile("https://issuer.example", "keyed-subject", "first", []), "create-123");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal((0, 0, 0), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task CanceledBusinessOperationLeavesNoIdempotencyClaim()
    {
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, configureServices: services =>
        {
            services.RemoveAll<IIntegrationEventPublisher>();
            services.AddSingleton<IIntegrationEventPublisher>(
                new FailingPublisher(new OperationCanceledException("operation canceled")));
        });
        fixture.Authenticate("profiles.manage");

        using var response = await PostKeyedAsync(fixture.Client,
            new CreateProfile("https://issuer.example", "keyed-subject", "first", []), "create-123");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal((0, 0, 0), await fixture.CountWritesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClaimConflictUsesFreshScopeForReplayOrDifferentRequest(bool differentRequest)
    {
        var race = new ClaimRace();
        await using var fixture = await Fixture.CreateAsync(httpIdempotencyEnabled: true, configureServices: services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(provider => new ClaimRaceUnitOfWork(
                provider.GetRequiredService<SelfManagedUnitOfWork<SelfManagedReferenceDbContext>>(), race));
        });
        fixture.Authenticate("profiles.manage");
        var request = new CreateProfile("https://issuer.example", "keyed-subject", "first", ["reader"]);
        byte[]? winnerBody = null;
        Uri? winnerLocation = null;
        race.Winner = async () =>
        {
            using var winner = await PostKeyedAsync(fixture.Client,
                differentRequest ? request with { DisplayName = "winner" } : request, "create-123");
            Assert.Equal(HttpStatusCode.Created, winner.StatusCode);
            winnerBody = await winner.Content.ReadAsByteArrayAsync();
            winnerLocation = winner.Headers.Location;
        };

        using var contender = await PostKeyedAsync(fixture.Client, request, "create-123");

        if (differentRequest)
        {
            Assert.Equal(HttpStatusCode.Conflict, contender.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Created, contender.StatusCode);
            Assert.Equal(winnerLocation, contender.Headers.Location);
            Assert.Equal(winnerBody, await contender.Content.ReadAsByteArrayAsync());
        }
        Assert.Equal((1, 1, 1), await fixture.CountWritesAsync());
    }

    [Fact]
    public async Task InvalidQueryValuesAreRejectedBeforeRepositoryMethodsRun()
    {
        var profiles = Substitute.For<IProfileRepository>();
        var audits = Substitute.For<IProfileAuditRepository>();
        var failures = Substitute.For<IFailedMessageOperations>();
        await using var fixture = await Fixture.CreateAsync(configureServices: services =>
        {
            services.RemoveAll<IProfileRepository>();
            services.RemoveAll<IProfileAuditRepository>();
            services.RemoveAll<IFailedMessageOperations>();
            services.AddSingleton(profiles);
            services.AddSingleton(audits);
            services.AddSingleton(failures);
        });
        fixture.Authenticate("profiles.manage");
        foreach (var path in new[]
        {
            "/api/v1/profiles?skip=-1", "/api/v1/profiles?take=0", "/api/v1/profiles?take=201",
            "/api/v1/profiles?take=invalid", "/api/v1/audit?take=0", "/api/v1/audit?take=201"
        })
        {
            using var response = await fixture.Client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{path} returned {(int)response.StatusCode}.");
        }

        fixture.Authenticate("messaging.manage");
        foreach (var path in new[] { "/api/operations/messages/failures?limit=0", "/api/operations/messages/failures?limit=1001" })
        {
            using var response = await fixture.Client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{path} returned {(int)response.StatusCode}.");
        }

        _ = profiles.DidNotReceiveWithAnyArgs().ListAsync(default, default, default);
        _ = audits.DidNotReceiveWithAnyArgs().ListAsync(default, default, default);
        _ = failures.DidNotReceiveWithAnyArgs().ListAsync(default, default);
    }

    [Fact]
    public async Task QueryDefaultsNormalValuesAndInclusiveLimitsReachRepositories()
    {
        var profiles = Substitute.For<IProfileRepository>();
        profiles.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserProfile>>([]));
        var audits = Substitute.For<IProfileAuditRepository>();
        audits.ListAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ProfileAuditEntry>>([]));
        var failures = Substitute.For<IFailedMessageOperations>();
        failures.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FailedMessage>>([]));
        await using var fixture = await Fixture.CreateAsync(configureServices: services =>
        {
            services.RemoveAll<IProfileRepository>();
            services.RemoveAll<IProfileAuditRepository>();
            services.RemoveAll<IFailedMessageOperations>();
            services.AddSingleton(profiles);
            services.AddSingleton(audits);
            services.AddSingleton(failures);
        });
        fixture.Authenticate("profiles.manage");
        using var profileDefault = await fixture.Client.GetAsync("/api/v1/profiles");
        using var profileNormal = await fixture.Client.GetAsync("/api/v1/profiles?skip=7&take=25");
        using var profileMax = await fixture.Client.GetAsync("/api/v1/profiles?skip=2147483647&take=200");
        using var auditDefault = await fixture.Client.GetAsync("/api/v1/audit");
        var profileId = Guid.NewGuid();
        using var auditMax = await fixture.Client.GetAsync($"/api/v1/audit?profileId={profileId}&take=200");
        Assert.All(new[] { profileDefault, profileNormal, profileMax, auditDefault, auditMax },
            response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        _ = profiles.Received(1).ListAsync(0, 50, Arg.Any<CancellationToken>());
        _ = profiles.Received(1).ListAsync(7, 25, Arg.Any<CancellationToken>());
        _ = profiles.Received(1).ListAsync(int.MaxValue, 200, Arg.Any<CancellationToken>());
        _ = audits.Received(1).ListAsync(null, 50, Arg.Any<CancellationToken>());
        _ = audits.Received(1).ListAsync(profileId, 200, Arg.Any<CancellationToken>());

        fixture.Authenticate("messaging.manage");
        using var failuresDefault = await fixture.Client.GetAsync("/api/operations/messages/failures");
        using var failuresNormal = await fixture.Client.GetAsync("/api/operations/messages/failures?limit=5");
        using var failuresMax = await fixture.Client.GetAsync("/api/operations/messages/failures?limit=1000");
        Assert.All(new[] { failuresDefault, failuresNormal, failuresMax },
            response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        _ = failures.Received(1).ListAsync(100, Arg.Any<CancellationToken>());
        _ = failures.Received(1).ListAsync(5, Arg.Any<CancellationToken>());
        _ = failures.Received(1).ListAsync(1000, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DomainValidationStillMapsToPublicProblemDetails()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Authenticate("profiles.manage");
        using var response = await fixture.Client.PostAsJsonAsync("/api/v1/profiles", new CreateProfile("", "subject", "name", []));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemCodeAsync(response, "validation");
    }

    private static async Task AssertProblemCodeAsync(HttpResponseMessage response, string code)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task ReadinessRejectsAnUnmigratedDatabase()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var response = await fixture.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unhealthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("pending_migrations", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal(2, body.RootElement.EnumerateObject().Count());
    }

    [Theory]
    [InlineData(false, HttpStatusCode.OK)]
    [InlineData(true, HttpStatusCode.ServiceUnavailable)]
    public async Task IdempotencyMigrationIsRequiredForReadinessOnlyWhenEnabled(bool enabled, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.CreateAsync(
            brokerAvailable: true, onlyIdempotencyMigrationPending: true, httpIdempotencyEnabled: enabled);
        await fixture.ExecuteSqlAsync("DROP TABLE HttpIdempotency");

        using var response = await fixture.Client.GetAsync("/health/ready");
        Assert.Equal(expected, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(enabled ? "unhealthy" : "healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(enabled ? "pending_migrations" : "SelfManaged",
            body.RootElement.GetProperty(enabled ? "reason" : "messaging").GetString());
    }

    [Theory]
    [InlineData(true, "healthy")]
    [InlineData(false, "degraded")]
    public async Task BrokerOutageDegradesReadinessWithoutRejectingDurableBusinessWrites(bool connected, string expected)
    {
        await using var fixture = await Fixture.CreateAsync(migrated: true, brokerAvailable: connected);
        using var response = await fixture.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, body.RootElement.GetProperty("status").GetString());
        Assert.Equal("SelfManaged", body.RootElement.GetProperty("messaging").GetString());
        Assert.Equal(2, body.RootElement.EnumerateObject().Count());
    }

    [Theory]
    [InlineData(false, "pending_migrations")]
    [InlineData(true, "storage_unavailable")]
    public async Task StorageFailureIsUnhealthyAndKeepsMigrationReasonPriority(bool migrated, string reason)
    {
        await using var fixture = await Fixture.CreateAsync(migrated: migrated, storageAvailable: false);
        using var response = await fixture.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unhealthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(reason, body.RootElement.GetProperty("reason").GetString());
        Assert.Equal(2, body.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task BrokerProbeExceptionIsUnhealthyRatherThanDegraded()
    {
        await using var fixture = await Fixture.CreateAsync(migrated: true, brokerAvailable: true);
        await fixture.Broker.DisposeAsync();
        using var response = await fixture.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unhealthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("storage_unavailable", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task AnonymousAndForbiddenRequestsDoNotConsumeAnAuthorizedUsersQuota()
    {
        await using var fixture = await Fixture.CreateAsync(apiPermitLimit: 2);
        for (var i = 0; i < 3; i++)
        {
            using var health = await fixture.Client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        }

        for (var i = 0; i < 4; i++)
        {
            using var anonymous = await fixture.Client.GetAsync("/api/v1/profiles");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        fixture.Authenticate("messaging.manage", "forbidden-user");
        for (var i = 0; i < 4; i++)
        {
            using var forbidden = await fixture.Client.GetAsync("/api/v1/roles");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        fixture.Authenticate("profiles.manage", "authorized-user");
        using var first = await fixture.Client.GetAsync("/api/v1/roles");
        using var second = await fixture.Client.GetAsync("/api/v1/roles");
        using var rejected = await fixture.Client.GetAsync("/api/v1/roles");
        using var unknown = await fixture.Client.GetAsync("/not-found");
        using var healthAfter = await fixture.Client.GetAsync("/health/live");
        using var readiness = await fixture.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.OK, healthAfter.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedSubjectsHaveIndependentRateLimitPartitions()
    {
        await using var fixture = await Fixture.CreateAsync(apiPermitLimit: 2);
        fixture.Authenticate("profiles.manage", "user-a");

        using var firstA = await fixture.Client.GetAsync("/api/v1/roles");
        using var secondA = await fixture.Client.GetAsync("/api/v1/roles");
        using var rejectedA = await fixture.Client.GetAsync("/api/v1/roles");
        Assert.Equal(HttpStatusCode.OK, firstA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondA.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedA.StatusCode);

        fixture.Authenticate("profiles.manage", "user-b");
        using var firstB = await fixture.Client.GetAsync("/api/v1/roles");
        using var secondB = await fixture.Client.GetAsync("/api/v1/roles");
        using var rejectedB = await fixture.Client.GetAsync("/api/v1/roles");
        Assert.Equal(HttpStatusCode.OK, firstB.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondB.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedB.StatusCode);

        fixture.Authenticate("profiles.manage", "user-a");
        using var changedHeader = new HttpRequestMessage(HttpMethod.Get, "/api/v1/roles");
        changedHeader.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.50");
        using var stillRejectedA = await fixture.Client.SendAsync(changedHeader);
        Assert.Equal(HttpStatusCode.TooManyRequests, stillRejectedA.StatusCode);
    }

    [Fact]
    public async Task IdentityPartitionReceivesNewPermitAfterItsFixedWindow()
    {
        await using var fixture = await Fixture.CreateAsync(apiPermitLimit: 1, apiWindowSeconds: 2);
        fixture.Authenticate("profiles.manage", "window-user");

        using var first = await fixture.Client.GetAsync("/api/v1/roles");
        using var rejected = await fixture.Client.GetAsync("/api/v1/roles");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        await Task.Delay(TimeSpan.FromMilliseconds(2500));
        using var restored = await fixture.Client.GetAsync("/api/v1/roles");
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    [Theory]
    [InlineData("invalid", "120", "60")]
    [InlineData("true", "0", "60")]
    [InlineData("true", "120", "0")]
    public void InvalidRateLimitConfigurationFailsDuringComposition(string enabled, string permitLimit, string windowSeconds)
    {
        var builder = Fixture.Builder();
        builder.Configuration["Http:RateLimiting:Enabled"] = enabled;
        builder.Configuration["Http:RateLimiting:PermitLimit"] = permitLimit;
        builder.Configuration["Http:RateLimiting:WindowSeconds"] = windowSeconds;
        Assert.Throws<ArgumentException>(() => ReferenceHost.AddServices(builder));
    }

    [Fact]
    public async Task RequestTimeoutPolicyAppliesToApiButNotHealth()
    {
        await using var fixture = await Fixture.CreateAsync(apiTimeoutSeconds: 5);
        var api = fixture.Endpoints.OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/api/v1/roles");
        var live = fixture.Endpoints.OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/health/live");
        var ready = fixture.Endpoints.OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/health/ready");
        Assert.Equal("reference-api-timeout", api.Metadata.GetMetadata<RequestTimeoutAttribute>()?.PolicyName);
        Assert.Null(live.Metadata.GetMetadata<RequestTimeoutAttribute>());
        Assert.Null(ready.Metadata.GetMetadata<RequestTimeoutAttribute>());

        fixture.Authenticate("profiles.manage");
        using var response = await fixture.Client.GetAsync("/api/v1/roles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("invalid", "30")]
    [InlineData("true", "0")]
    [InlineData("true", "-1")]
    public void InvalidRequestTimeoutConfigurationFailsDuringComposition(string enabled, string seconds)
    {
        var builder = Fixture.Builder();
        builder.Configuration["Http:RequestTimeouts:Enabled"] = enabled;
        builder.Configuration["Http:RequestTimeouts:Seconds"] = seconds;
        Assert.Throws<ArgumentException>(() => ReferenceHost.AddServices(builder));
    }

    [Fact]
    public void UnknownProviderFailsDuringComposition()
    {
        var builder = Fixture.Builder("unknown");
        Assert.Throws<ArgumentException>(() => ReferenceHost.AddServices(builder));
    }

    private static Task<HttpResponseMessage> PostKeyedAsync(HttpClient client, CreateProfile body,
        params string[] keys)
        => PostKeyedToAsync(client, "/api/v1/profiles", body, keys);

    private static async Task<HttpResponseMessage> PostKeyedToAsync(HttpClient client, string path, CreateProfile body,
        params string[] keys)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        Assert.True(request.Headers.TryAddWithoutValidation("Idempotency-Key", keys));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostKeyedOrderAsync(HttpClient client, CreateOrder body, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private sealed class FailingPublisher(Exception failure) : IIntegrationEventPublisher
    {
        public ValueTask EnqueueAsync(IIntegrationEvent message, CancellationToken cancellationToken = default)
            => throw failure;
    }

    private sealed class ClaimRace
    {
        internal Func<Task>? Winner { get; set; }
        internal int Triggered;
    }

    private sealed class ClaimRaceUnitOfWork(
        SelfManagedUnitOfWork<SelfManagedReferenceDbContext> inner, ClaimRace race) : IUnitOfWork
    {
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref race.Triggered, 1) == 0)
                await (race.Winner ?? throw new InvalidOperationException("The winner request was not configured."))();
            return await inner.ExecuteAsync(operation, cancellationToken);
        }
    }

    private sealed class Fixture(WebApplication app, SqliteConnection connection) : IAsyncDisposable
    {
        private static readonly SymmetricSecurityKey Key = new(Enumerable.Repeat((byte)19, 32).ToArray());
        public HttpClient Client { get; } = app.GetTestClient();
        public RabbitMqTransport Broker => app.Services.GetRequiredService<RabbitMqTransport>();
        public IReadOnlyList<Endpoint> Endpoints => app.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        public static WebApplicationBuilder Builder(string provider = "SelfManaged") => ServiceHost.CreateBuilder([
            "--environment", "Production", "--ConnectionStrings:ReferenceDatabase", "Host=unused;Database=reference;Username=test",
            "--Messaging:Provider", provider, "--Messaging:RabbitMQ:ConnectionString", "amqp://localhost",
            "--Authentication:Authority", "https://issuer.example", "--Authentication:Audience", "ms-reference",
            "--OpenTelemetry:Enabled", "false"
        ]);

        public static async Task<Fixture> CreateAsync(bool migrated = false, bool brokerAvailable = false,
            int? apiPermitLimit = null, int apiWindowSeconds = 60, int? apiTimeoutSeconds = null, bool storageAvailable = true,
            Action<IServiceCollection>? configureServices = null, bool? httpIdempotencyEnabled = null,
            bool onlyIdempotencyMigrationPending = false, bool mvcEndpoints = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var builder = Builder();
            if (httpIdempotencyEnabled is { } enabled)
                builder.Configuration["Http:Idempotency:Enabled"] = enabled.ToString();
            if (apiPermitLimit is { } limit)
            {
                builder.Configuration["Http:RateLimiting:Enabled"] = "true";
                builder.Configuration["Http:RateLimiting:PermitLimit"] = limit.ToString();
                builder.Configuration["Http:RateLimiting:WindowSeconds"] = apiWindowSeconds.ToString();
            }
            if (apiTimeoutSeconds is { } seconds)
            {
                builder.Configuration["Http:RequestTimeouts:Enabled"] = "true";
                builder.Configuration["Http:RequestTimeouts:Seconds"] = seconds.ToString();
            }
            builder.WebHost.UseTestServer();
            ReferenceHost.AddServices(builder);
            foreach (var descriptor in builder.Services.Where(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType?.Namespace?.StartsWith("MS.Microservice.Messaging", StringComparison.Ordinal) == true
                || descriptor.ServiceType.IsGenericType && descriptor.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
                    && descriptor.ServiceType.GenericTypeArguments.Contains(typeof(SelfManagedReferenceDbContext))).ToArray())
                builder.Services.Remove(descriptor);
            builder.Services.RemoveAll<DbContextOptions<SelfManagedReferenceDbContext>>();
            builder.Services.RemoveAll<SelfManagedReferenceDbContext>();
            builder.Services.AddDbContext<SelfManagedReferenceDbContext>(options => options.UseSqlite(connection));
            if (!storageAvailable)
            {
                builder.Services.RemoveAll<IMessageStorageProbe>();
                var storage = Substitute.For<IMessageStorageProbe>();
                storage.CheckAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("storage offline")));
                builder.Services.AddSingleton(storage);
            }
            builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = "https://issuer.example" };
                configuration.SigningKeys.Add(Key);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
            var brokerFactory = Substitute.For<IConnectionFactory>();
            if (brokerAvailable)
            {
                var brokerConnection = Substitute.For<IConnection>();
                var channel = Substitute.For<IChannel>();
                channel.IsOpen.Returns(true);
                brokerConnection.CreateChannelAsync(Arg.Any<CreateChannelOptions>(), Arg.Any<CancellationToken>()).Returns(channel);
                brokerFactory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(brokerConnection);
            }
            else brokerFactory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<IConnection>(new IOException("offline")));
            builder.Services.AddSingleton(brokerFactory);
            if (mvcEndpoints)
            {
                builder.Services.AddControllers().AddApplicationPart(typeof(ProfileMvcTestController).Assembly);
                builder.Services.AddScoped<ReferenceHttpIdempotencyResourceFilter>();
            }
            configureServices?.Invoke(builder.Services);
            var app = builder.Build();
            ReferenceHost.MapApplication(app);
            if (mvcEndpoints) app.MapControllers();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<SelfManagedReferenceDbContext>();
                await context.Database.EnsureCreatedAsync();
                if (migrated || onlyIdempotencyMigrationPending)
                {
                    var history = context.GetService<IHistoryRepository>();
                    await context.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript());
                    foreach (var migration in context.Database.GetMigrations().Where(migration =>
                        !onlyIdempotencyMigrationPending || !migration.EndsWith("_AddHttpIdempotency", StringComparison.Ordinal)))
                        await context.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(migration, "10.0.6")));
                }
            }
            await app.StartAsync();
            return new(app, connection);
        }

        public void Authenticate(string scope, string subject = "test-admin", string? tokenId = null)
        {
            var claims = new List<Claim> { new("sub", subject), new("scope", scope) };
            if (tokenId is not null) claims.Add(new("jti", tokenId));
            var token = new JwtSecurityToken("https://issuer.example", "ms-reference",
                claims, expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new(Key, SecurityAlgorithms.HmacSha256));
            Client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }

        public async Task<(int Profiles, int Outbox, int Idempotency)> CountWritesAsync()
        {
            await using var scope = app.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SelfManagedReferenceDbContext>();
            return (await context.Profiles.CountAsync(),
                await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Outbox").SingleAsync(),
                await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM HttpIdempotency").SingleAsync());
        }

        public async Task ExecuteSqlAsync(string sql)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SelfManagedReferenceDbContext>();
            await context.Database.ExecuteSqlRawAsync(sql);
        }

        public async Task<int> CountProfilesAsync()
        {
            await using var scope = app.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<SelfManagedReferenceDbContext>()
                .Profiles.CountAsync();
        }

        public async Task<int> CountOrdersAsync()
        {
            await using var scope = app.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SelfManagedReferenceDbContext>();
            return await context.Orders.CountAsync();
        }

        public async Task SeedIdempotencyAsync(IdempotencyRecord record)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SelfManagedReferenceDbContext>();
            context.Set<IdempotencyRecord>().Add(record);
            await context.SaveChangesAsync();
        }

        public async Task<bool> HasIdempotencyTableAsync()
        {
            await using var scope = app.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<SelfManagedReferenceDbContext>();
            var count = await context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'HttpIdempotency'")
                .SingleAsync();
            return count != 0;
        }

        public async Task<int> PruneExpiredAsync()
        {
            await using var scope = app.Services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<EfCoreIdempotencyStore<ReferenceDbContext>>();
            return await store.PruneExpiredAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
