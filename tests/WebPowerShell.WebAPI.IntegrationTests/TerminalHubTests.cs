using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WebPowerShell.Application.Users.Commands.Login;
using WebPowerShell.Domain.Common;
using WebPowerShell.Application.Common.Models;
using WebPowerShell.Domain.Entities;
using WebPowerShell.Infrastructure.ConPTY;
using WebPowerShell.Infrastructure.Security;
using WebPowerShell.WebAPI.Hubs;
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

    private async Task<HubConnection> CreateAuthenticatedConnectionAsync(string username, string password)
    {
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
        request.Headers.Add("X-Forwarded-For", $"127.0.0.{Random.Shared.Next(2, 254)}");

        var loginResponse = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

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

        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/terminal", options =>
            {
                options.HttpMessageHandlerFactory = _ => new CookieHandler(_factory.Server.CreateHandler(), cookieContainer);
            })
            .Build();

        await connection.StartAsync();
        return connection;
    }

    [Fact]
    public async Task Scenario1_UnauthenticatedAccess_ShouldBeBlocked()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/terminal", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
    }

    [Fact]
    public async Task Scenario2_AuthenticatedAccess_ShouldSucceed()
    {
        var connection = await CreateAuthenticatedConnectionAsync("hubtestuser", "CorrectPassword123!");

        Assert.Equal(HubConnectionState.Connected, connection.State);

        await connection.StopAsync();
    }

    [Fact]
    public async Task Scenario3_SessionOwnershipBlocked_ShouldFail()
    {
        var tabId = Guid.NewGuid();
        var connectionA = await CreateAuthenticatedConnectionAsync("userA", "CorrectPassword123!");
        var connectionB = await CreateAuthenticatedConnectionAsync("userB", "CorrectPassword123!");

        try
        {
            var openResult = await connectionA.InvokeAsync<HubResponse>("CreateSession", tabId);
            Assert.True(openResult.Success, $"CreateSession failed: {openResult.ErrorCode}");

            var sendResult = await connectionB.InvokeAsync<HubResponse>("SendInput", tabId, Convert.ToBase64String(new byte[] { 65 }));
            Assert.False(sendResult.Success);
            Assert.Equal("Unauthorized", sendResult.ErrorCode);

            var closeResult = await connectionB.InvokeAsync<HubResponse>("CloseSession", tabId);
            Assert.False(closeResult.Success);
            Assert.Equal("Unauthorized", closeResult.ErrorCode);
        }
        finally
        {
            await connectionA.StopAsync();
            await connectionB.StopAsync();
        }
    }

    [Fact]
    public async Task Scenario4_NormalCommandExecution_ShouldSucceed()
    {
        var tabId = Guid.NewGuid();
        var connectionA = await CreateAuthenticatedConnectionAsync("userA2", "CorrectPassword123!");

        try
        {
            var openResult = await connectionA.InvokeAsync<HubResponse>("CreateSession", tabId);
            Assert.True(openResult.Success, $"CreateSession failed: {openResult.ErrorCode}");

            var sendResult = await connectionA.InvokeAsync<HubResponse>("SendInput", tabId, Convert.ToBase64String(new byte[] { 65 }));
            Assert.True(sendResult.Success);

            var closeResult = await connectionA.InvokeAsync<HubResponse>("CloseSession", tabId);
            Assert.True(closeResult.Success);
        }
        finally
        {
            await connectionA.StopAsync();
        }
    }
}
