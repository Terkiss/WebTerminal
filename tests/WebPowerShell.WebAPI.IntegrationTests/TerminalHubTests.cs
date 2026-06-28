using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WebPowerShell.Application.Users.Commands.Login;
using WebPowerShell.Domain.Entities;
using WebPowerShell.Infrastructure.Security;
using Xunit;

namespace WebPowerShell.WebAPI.IntegrationTests;

public class TerminalHubTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly BCryptPasswordHasher _passwordHasher;

    public TerminalHubTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _passwordHasher = new BCryptPasswordHasher(Substitute.For<ILogger<BCryptPasswordHasher>>());
    }

    private async Task<User> SeedUserHelperAsync(string username, string plaintextPassword)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = _passwordHasher.HashPassword(plaintextPassword),
            IsActive = true,
            CreatedAt = _factory.TimeProvider.GetUtcNow(),
            UpdatedAt = _factory.TimeProvider.GetUtcNow(),
            LastPasswordChangeDate = _factory.TimeProvider.GetUtcNow(),
            FailedLoginCount = 0,
            LockedUntil = null
        };

        await _factory.SeedUserAsync(user);
        return user;
    }

    // Helper handler to forward cookies over the TestServer ClientHandler
    private class CookieHandler : DelegatingHandler
    {
        private readonly CookieContainer _cookieContainer;

        public CookieHandler(HttpMessageHandler innerHandler, CookieContainer cookieContainer)
            : base(innerHandler)
        {
            _cookieContainer = cookieContainer;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cookieHeader = _cookieContainer.GetCookieHeader(request.RequestUri!);
            if (!string.IsNullOrEmpty(cookieHeader))
            {
                request.Headers.Add("Cookie", cookieHeader);
            }
            return base.SendAsync(request, cancellationToken);
        }
    }

    [Fact]
    public async Task Scenario1_UnauthenticatedAccess_ShouldBeBlocked()
    {
        // Arrange
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/terminal", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        // Act & Assert
        // Without authentication cookie, the connection should be rejected with an Exception.
        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
    }

    [Fact]
    public async Task Scenario2_AuthenticatedAccess_ShouldSucceed()
    {
        // Arrange
        var username = "hubtestuser";
        var password = "CorrectPassword123!";
        await SeedUserHelperAsync(username, password);

        var loginCommand = new LoginCommand
        {
            Username = username,
            Password = password
        };

        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(loginCommand)
        };
        request.Headers.Add("X-Forwarded-For", "127.0.0.1");

        // Act - Login
        var loginResponse = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // Extract cookie
        var cookieContainer = new CookieContainer();
        if (loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookieHeader in cookies)
            {
                var parts = cookieHeader.Split(';');
                if (parts.Length > 0)
                {
                    var cookieKeyValue = parts[0].Split('=');
                    if (cookieKeyValue.Length == 2)
                    {
                        var name = cookieKeyValue[0].Trim();
                        var value = cookieKeyValue[1].Trim();
                        cookieContainer.Add(new Cookie(name, value, "/", "localhost"));
                    }
                }
            }
        }

        // Configure SignalR Connection with the CookieHandler wrapping TestServer's ClientHandler
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/terminal", options =>
            {
                options.HttpMessageHandlerFactory = _ => new CookieHandler(_factory.Server.CreateHandler(), cookieContainer);
            })
            .Build();

        // Act - Start connection
        await connection.StartAsync();

        // Assert
        Assert.Equal(HubConnectionState.Connected, connection.State);

        // Cleanup
        await connection.StopAsync();
    }
}
