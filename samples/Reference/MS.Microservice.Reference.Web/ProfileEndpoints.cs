using System.Security.Claims;
using MS.Microservice.AspNetCore;
using MS.Microservice.Core.Functional;
using MS.Microservice.Messaging;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Domain;

namespace MS.Microservice.Reference.Web;

internal static class ProfileEndpoints
{
    public static void Map(WebApplication app)
    {
        var profiles = app.MapGroup("/api/v1/profiles").RequireAuthorization("Manage");
        profiles.MapPost("", async (CreateProfile request, ProfileService service, HttpContext http,
            ExternalIdentityOptions identity, CancellationToken token) =>
            Respond(await service.CreateAsync(request, Actor(http.User, identity), token),
                profile => Results.Created($"/api/v1/profiles/{profile.Id}", profile)));
        profiles.MapGet("", async (IProfileRepository repository, int? skip, int? take, CancellationToken token) =>
        {
            if (skip is < 0 || take is < 1 or > 200) return InvalidPagination();
            return Results.Ok((await repository.ListAsync(skip ?? 0, take ?? 50, token)).Select(ProfileView.From));
        });
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
        app.MapGet("/api/v1/audit", async (IProfileAuditRepository repository, Guid? profileId, int? take, CancellationToken token) =>
        {
            if (take is < 1 or > 200) return InvalidPagination();
            return Results.Ok(await repository.ListAsync(profileId, take ?? 50, token));
        }).RequireAuthorization("Manage");
        var operations = app.MapGroup("/api/operations/messages").RequireAuthorization("MessagingOperations");
        operations.MapGet("/failures", async (IFailedMessageOperations failures, int? limit, CancellationToken token) =>
        {
            if (limit is < 1 or > 1000) return InvalidPagination();
            return Results.Ok(await failures.ListAsync(limit ?? 100, token));
        });
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
    private static IResult InvalidPagination() => Results.Problem(statusCode: 400, title: "Pagination is outside the supported range.");
    private static IResult Respond<T>(Either<Error, T> result, Func<T, IResult> success)
        => result.Match(error => Results.Problem(statusCode: error.Code switch
        {
            "validation" => 400, "not_found" => 404, "conflict" => 409, "unauthorized" => 401, _ => 500
        }, title: error.Message, extensions: new Dictionary<string, object?> { ["code"] = error.Code }), success);
}
