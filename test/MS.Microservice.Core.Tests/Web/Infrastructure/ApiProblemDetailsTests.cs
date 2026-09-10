using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using MS.Microservice.Core.Functional;
using MS.Microservice.Domain.Exception;
using MS.Microservice.Domain.Services.Interfaces;
using MS.Microservice.Lab.Application.Commands;
using MS.Microservice.Lab.Application.Identity;
using MS.Microservice.Lab.Application.Models.AccountRequests;
using MS.Microservice.Lab.Application.Users;
using MS.Microservice.Lab.Controller;
using MS.Microservice.Lab.Infrastructure.Filters;
using MS.Microservice.Lab.Infrastructure.Http;
using NSubstitute;
using System.Text;
using System.Text.Json;

namespace MS.Microservice.Core.Tests.Web.Infrastructure;

public class ApiProblemDetailsTests
{
    [Theory]
    [InlineData("validation", StatusCodes.Status400BadRequest)]
    [InlineData("unauthorized", StatusCodes.Status401Unauthorized)]
    [InlineData("not_found", StatusCodes.Status404NotFound)]
    [InlineData("conflict", StatusCodes.Status409Conflict)]
    [InlineData("unexpected", StatusCodes.Status500InternalServerError)]
    public void FromError_MapsStableCodeToHttpStatus(string code, int expectedStatus)
    {
        var context = CreateHttpContext();
        var error = new Error(code, "safe message", ["safe detail"]);

        var problem = ApiProblemDetails.FromError(context, error);

        Assert.Equal(expectedStatus, problem.Status);
        Assert.Equal(code, problem.Extensions["code"]);
        Assert.Equal("trace-123", problem.Extensions["traceId"]);
        if (expectedStatus >= 500)
        {
            Assert.Equal(ApiProblemDetails.UnexpectedDetail, problem.Detail);
            Assert.DoesNotContain("errors", problem.Extensions.Keys);
        }
        else
        {
            Assert.Equal("safe message", problem.Detail);
            Assert.True(problem.Extensions.ContainsKey("errors"));
        }
    }

    [Fact]
    public async Task GlobalExceptionHandler_WhenExceptionIsUnexpected_HidesRawMessage()
    {
        var context = CreateHttpContext();
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("database-secret-details"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(ApiProblemDetails.ContentType, context.Response.ContentType);
        using var document = await ReadResponseAsync(context);
        Assert.Equal(ApiProblemDetails.UnexpectedDetail, document.RootElement.GetProperty("detail").GetString());
        Assert.Equal("unexpected", document.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("database-secret-details", document.RootElement.GetRawText());
    }

    [Fact]
    public async Task GlobalExceptionHandler_WhenDomainExceptionHasHttpCode_UsesThatCode()
    {
        var context = CreateHttpContext();
        context.Response.Body = new MemoryStream();
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);

        await handler.TryHandleAsync(
            context,
            new DomainException(StatusCodes.Status409Conflict, "用户已存在"),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        using var document = await ReadResponseAsync(context);
        Assert.Equal("用户已存在", document.RootElement.GetProperty("detail").GetString());
        Assert.Equal("domain_error", document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AccountLogin_WhenPasswordEncodingIsInvalid_ReturnsBadRequestProblem()
    {
        var controller = CreateAccountController();

        var result = await controller.Login(new LoginRequest
        {
            Account = "demo",
            Password = "not-base64"
        });

        AssertProblem(result, StatusCodes.Status400BadRequest, "validation");
    }

    [Fact]
    public async Task AccountLogin_WhenCredentialsAreInvalid_ReturnsUnauthorizedProblem()
    {
        var userDomainService = Substitute.For<IUserDomainService>();
        userDomainService
            .FindAsync("demo", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MS.Microservice.Domain.Aggregates.IdentityModel.User?>(null));
        var controller = CreateAccountController(userDomainService);

        var result = await controller.Login(new LoginRequest
        {
            Account = "demo",
            Password = Convert.ToBase64String(Encoding.UTF8.GetBytes("Password123"))
        });

        AssertProblem(result, StatusCodes.Status401Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task UserCreate_WhenApplicationReturnsConflict_ReturnsConflictProblem()
    {
        var createService = Substitute.For<IUserCreateAppService>();
        createService
            .CreateAsync(Arg.Any<UserCreatedCommand>(), Arg.Any<CancellationToken>())
            .Returns((Either<Error, bool>)F.Left(Error.Conflict("用户已存在")));
        var controller = new UserController(null!, null!, createService, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = CreateHttpContext() }
        };

        var result = await controller.CreateUser(new UserCreatedCommand(
            "demo",
            "Demo",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("Password123")),
            "13800138000",
            "demo@example.com",
            []));

        AssertProblem(result, StatusCodes.Status409Conflict, "conflict");
    }

    [Fact]
    public async Task UserModifyFunctional_WhenUserIsMissing_ReturnsNotFoundProblem()
    {
        var modifyService = Substitute.For<IUserModifyAppService>();
        modifyService
            .ModifyAsync(Arg.Any<UserModifyCommand>(), Arg.Any<CancellationToken>())
            .Returns((Either<Error, bool>)F.Left(Error.NotFound("用户不存在")));
        var controller = new UserController(null!, null!, null!, modifyService)
        {
            ControllerContext = new ControllerContext { HttpContext = CreateHttpContext() }
        };

        var result = await controller.ModifyFunctional(new UserModifyCommand(
            "demo",
            "Demo",
            string.Empty,
            "13800138000",
            "demo@example.com",
            []));

        AssertProblem(result, StatusCodes.Status404NotFound, "not_found");
    }

    private static AccountController CreateAccountController(IUserDomainService? userDomainService = null)
    {
        var controller = new AccountController(
            null!,
            null!,
            userDomainService ?? Substitute.For<IUserDomainService>(),
            Substitute.For<IUserPasswordService>(),
            Substitute.For<IDistributedCache>())
        {
            ControllerContext = new ControllerContext { HttpContext = CreateHttpContext() }
        };
        return controller;
    }

    private static DefaultHttpContext CreateHttpContext()
        => new()
        {
            TraceIdentifier = "trace-123"
        };

    private static void AssertProblem(IActionResult result, int expectedStatus, string expectedCode)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        Assert.Contains(ApiProblemDetails.ContentType, objectResult.ContentTypes);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(expectedStatus, problem.Status);
        Assert.Equal(expectedCode, problem.Extensions["code"]);
    }

    private static async Task<JsonDocument> ReadResponseAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }
}
