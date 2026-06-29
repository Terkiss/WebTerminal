using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WebPowerShell.Application.Common.Interfaces;
using WebPowerShell.Application.Users.Commands.Login;
using WebPowerShell.Domain.Common;
using WebPowerShell.Domain.Entities;
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
            var openResult = await connectionA.InvokeAsync<HubResponse>("OpenTab", tabId);
            Assert.True(openResult.Success);

            var sendResult = await connectionB.InvokeAsync<HubResponse>("SendCommand", tabId, "Get-Process");
            Assert.False(sendResult.Success);
            Assert.Equal("SessionNotFound", sendResult.ErrorCode);

            var closeResult = await connectionB.InvokeAsync<HubResponse>("CloseTab", tabId);
            Assert.False(closeResult.Success);
            Assert.Equal("SessionNotFound", closeResult.ErrorCode);
        }
        finally
        {
            await connectionA.StopAsync();
            await connectionB.StopAsync();
        }
    }

    [Fact]
    public async Task Scenario4_NormalCommandExecutionAndStop_ShouldSucceed()
    {
        var tabId = Guid.NewGuid();
        var connectionA = await CreateAuthenticatedConnectionAsync("userA2", "CorrectPassword123!");

        try
        {
            var openResult = await connectionA.InvokeAsync<HubResponse>("OpenTab", tabId);
            Assert.True(openResult.Success);

            var sendResult = await connectionA.InvokeAsync<HubResponse>("SendCommand", tabId, "Write-Output 'Hello'");
            Assert.True(sendResult.Success);

            var stopResult = await connectionA.InvokeAsync<HubResponse>("StopCommand", tabId);
            Assert.True(stopResult.Success);
        }
        finally
        {
            await connectionA.StopAsync();
        }
    }

    [Fact]
    public async Task Scenario5_HubUnhandledException_ShouldReturnInternalError()
    {
        var mockedService = Substitute.For<IPowerShellSessionService>();
        mockedService
            .CreateSessionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Func<string, Task>>(), Arg.Any<Func<string, Task>>(), Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<PowerShellSession>>>(x => throw new InvalidOperationException("Simulated unhandled exception"));

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPowerShellSessionService>();
                services.AddSingleton<IPowerShellSessionService>(mockedService);
            });
        });

        var username = "exceptionuser";
        var password = "CorrectPassword123!";

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = _passwordHasher.HashPassword(password),
            IsActive = true,
            CreatedAt = _factory.TimeProvider.GetUtcNow(),
            UpdatedAt = _factory.TimeProvider.GetUtcNow(),
            LastPasswordChangeDate = _factory.TimeProvider.GetUtcNow(),
            FailedLoginCount = 0,
            LockedUntil = null
        };
        await _factory.SeedUserAsync(user);

        var loginCommand = new LoginCommand { Username = username, Password = password };
        var client = factory.CreateClient();
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
                options.HttpMessageHandlerFactory = _ => new CookieHandler(factory.Server.CreateHandler(), cookieContainer);
            })
            .Build();

        await connection.StartAsync();

        try
        {
            var tabId = Guid.NewGuid();
            var openResult = await connection.InvokeAsync<HubResponse>("OpenTab", tabId);

            Assert.False(openResult.Success);
            Assert.Equal("InternalError", openResult.ErrorCode);
        }
        finally
        {
            await connection.StopAsync();
        }
    }

    [Fact]
    public async Task Scenario6_StreamingOutputRouting_ShouldSucceed()
    {
        var tabId = Guid.NewGuid();
        var connection = await CreateAuthenticatedConnectionAsync("streamuser1", "CorrectPassword123!");

        var outputTcs = new TaskCompletionSource<(Guid tabId, string content)>();

        connection.On<Guid, string>("ReceiveOutput", (id, content) =>
        {
            if (content.Contains("StreamingHello"))
                outputTcs.TrySetResult((id, content));
        });

        try
        {
            var openResult = await connection.InvokeAsync<HubResponse>("OpenTab", tabId);
            Assert.True(openResult.Success);

            var sendResult = await connection.InvokeAsync<HubResponse>("SendCommand", tabId, "echo 'StreamingHello'");
            Assert.True(sendResult.Success);

            var outputData = await outputTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(tabId, outputData.tabId);
            Assert.Contains("StreamingHello", outputData.content);
        }
        finally
        {
            await connection.StopAsync();
        }
    }

    [Fact]
    public async Task Scenario7_StreamingErrorRouting_ShouldSucceed()
    {
        var tabId = Guid.NewGuid();
        var connection = await CreateAuthenticatedConnectionAsync("streamuser2", "CorrectPassword123!");

        var errorTcs = new TaskCompletionSource<(Guid tabId, string content)>();

        connection.On<Guid, string>("ReceiveError", (id, content) =>
        {
            if (content.Contains("StreamingError"))
                errorTcs.TrySetResult((id, content));
        });

        try
        {
            var openResult = await connection.InvokeAsync<HubResponse>("OpenTab", tabId);
            Assert.True(openResult.Success);

            var sendResult = await connection.InvokeAsync<HubResponse>("SendCommand", tabId, "[Console]::Error.WriteLine('StreamingError')");
            Assert.True(sendResult.Success);

            var errorData = await errorTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(tabId, errorData.tabId);
            Assert.Contains("StreamingError", errorData.content);
        }
        finally
        {
            await connection.StopAsync();
        }
    }
}
