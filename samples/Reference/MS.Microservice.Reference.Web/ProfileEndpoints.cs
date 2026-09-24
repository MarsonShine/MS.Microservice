using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Routing;
using MS.Microservice.AspNetCore;
using MS.Microservice.Core.Functional;
using MS.Microservice.Idempotency.EFCore;
using MS.Microservice.Messaging;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Domain;
using MS.Microservice.Reference.Persistence;

namespace MS.Microservice.Reference.Web;

internal static class ProfileEndpoints
{
    public static void Map(IEndpointRouteBuilder app, bool idempotencyEnabled)
    {
        var profiles = app.MapGroup("/api/v1/profiles").RequireAuthorization("Manage");
        if (idempotencyEnabled)
        {
            profiles.MapPost("", async (CreateProfile request, ProfileService service, HttpContext http,
                ExternalIdentityOptions identity, IUnitOfWork unit, EfCoreIdempotencyStore<ReferenceDbContext> store,
                IServiceScopeFactory scopes, CancellationToken token) =>
                await ProfileIdempotencyHandler.CreateAsync(request, Actor(http.User, identity), service, http,
                    unit, store, scopes, token));
        }
        else
        {
            profiles.MapPost("", async (CreateProfile request, ProfileService service, HttpContext http,
                ExternalIdentityOptions identity, CancellationToken token) =>
                Respond(await service.CreateAsync(request, Actor(http.User, identity), token),
                    profile => Results.Created($"/api/v1/profiles/{profile.Id}", profile)));
        }
        profiles.MapGet("", async (IProfileRepository repository, CancellationToken token,
            [Range(0, int.MaxValue)] int skip = 0, [Range(1, 200)] int take = 50) =>
            Results.Ok((await repository.ListAsync(skip, take, token)).Select(ProfileView.From)));
        profiles.MapGet("/{id:guid}", async (Guid id, IProfileRepository repository, CancellationToken token) =>
            await repository.GetAsync(id, token) is { } profile ? Results.Ok(ProfileView.From(profile)) : Results.NotFound());
        profiles.MapPatch("/{id:guid}", async (Guid id, ChangeProfile request, ProfileService service, HttpContext http,
            ExternalIdentityOptions identity, CancellationToken token) =>
            Respond(await service.ChangeAsync(id, request, Actor(http.User, identity), token), Results.Ok));
        app.MapGet("/api/v1/roles", () => Results.Ok(ProfileRoles.All)).RequireAuthorization("Manage");
        app.MapGet("/api/v1/me", async (HttpContext http, ExternalIdentityOptions identity,
            IProfileRepository repository, CancellationToken token) =>
        {
            var actor = Actor(http.User, identity);
            var profile = await repository.FindAsync(actor.Issuer, actor.Subject, token);
            return profile is null ? Results.NotFound() : Results.Ok(ProfileView.From(profile));
        }).RequireAuthorization();
        app.MapGet("/api/v1/audit", async (IProfileAuditRepository repository, CancellationToken token,
            Guid? profileId = null, [Range(1, 200)] int take = 50) =>
            Results.Ok(await repository.ListAsync(profileId, take, token))).RequireAuthorization("Manage");
        var operations = app.MapGroup("/api/operations/messages").RequireAuthorization("MessagingOperations");
        operations.MapGet("/failures", async (IFailedMessageOperations failures, CancellationToken token,
            [Range(1, 1000)] int limit = 100) => Results.Ok(await failures.ListAsync(limit, token)));
        operations.MapPost("/failures/{failureId}/replay", async (string failureId, IFailedMessageOperations failures, CancellationToken token) =>
            await failures.ReplayAsync(failureId, token) switch
            {
                ReplayResult.Accepted => Results.Accepted(),
                ReplayResult.NotFound => Results.NotFound(),
                _ => Results.Conflict(new { code = "invalid_replay_state" })
            });
    }

    private static AuditActor Actor(ClaimsPrincipal user, ExternalIdentityOptions identity)
        => new(user.FindFirstValue("iss") ?? "", user.FindFirstValue(identity.SubjectClaimType) ?? "");
    private static IResult Respond<T>(Either<Error, T> result, Func<T, IResult> success)
        => result.Match(error => ApplicationErrorResults.ToProblem(error.Code, error.Message, error.Details), success);
}
