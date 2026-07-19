using System;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WebPowerShell.Application.Users.Commands.Login;
using WebPowerShell.Domain.Entities;
using WebPowerShell.Infrastructure.AgentRuntime;
using WebPowerShell.Infrastructure.Security;
using WebPowerShell.WebAPI.Controllers;

namespace WebPowerShell.WebAPI.IntegrationTests;

public sealed class AgentProviderApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly BCryptPasswordHasher _passwordHasher;

    public AgentProviderApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _passwordHasher = new BCryptPasswordHasher(Substitute.For<ILogger<BCryptPasswordHasher>>());
    }

    [Fact]
    public async Task ProviderSessionLifecycle_IssuesApiKeyAndRejectsAfterRevoke()
    {
        var client = _factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync("provider-admin", password, isAdmin: true);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginCommand
        {
            Username = "provider-admin",
            Password = password
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Integration Provider"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var created = await ReadJsonAsync(createResponse);
        var sessionId = created.RootElement.GetProperty("sessionId").GetString();
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sessionId));
        Assert.StartsWith("wta_", apiKey);

        using var modelsRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
        modelsRequest.Headers.Authorization = new("Bearer", apiKey);
        var modelsResponse = await client.SendAsync(modelsRequest);
        Assert.Equal(HttpStatusCode.OK, modelsResponse.StatusCode);

        var revokeResponse = await client.DeleteAsync($"/api/agent/provider-sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        using var rejectedModelsRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
        rejectedModelsRequest.Headers.Authorization = new("Bearer", apiKey);
        var rejectedModelsResponse = await client.SendAsync(rejectedModelsRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedModelsResponse.StatusCode);

        var auditLogs = await _factory.GetAuditLogsAsync();
        Assert.Contains(auditLogs, log => log.Command == "agent.provider.session.create" && log.ResultStatus == "Success");
        Assert.Contains(auditLogs, log => log.Command == "agent.provider.models" && log.ResultStatus == "Success");
        Assert.Contains(auditLogs, log => log.Command == "agent.provider.session.revoke" && log.ResultStatus == "Success");
    }

    [Fact]
    public async Task CreateProviderSession_RequiresAdminRole()
    {
        var client = _factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync("provider-user", password, isAdmin: false);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginCommand
        {
            Username = "provider-user",
            Password = password
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Forbidden Provider"
        });

        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
    }

    [Fact]
    public async Task ProviderApi_ReturnsTooManyRequestsAfterRateLimit()
    {
        var client = _factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync("provider-rate-admin", password, isAdmin: true);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginCommand
        {
            Username = "provider-rate-admin",
            Password = password
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Rate Limited Provider"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        HttpResponseMessage? response = null;
        for (var i = 0; i < 61; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
            request.Headers.Authorization = new("Bearer", apiKey);
            response = await client.SendAsync(request);
        }

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task InternalAgentEvents_AcceptsSignedEventAndRejectsNonceReplay()
    {
        var client = _factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync("provider-events-admin", password, isAdmin: true);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginCommand
        {
            Username = "provider-events-admin",
            Password = password
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Event Provider"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadJsonAsync(createResponse);
        var sessionId = created.RootElement.GetProperty("sessionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sessionId));
        var transcriptPath = Path.Combine(Path.GetTempPath(), $"webterminal-transcript-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllLinesAsync(transcriptPath, new[]
        {
            """{"role":"user","content":"hello"}""",
            """{"role":"assistant","content":"world"}"""
        });

        var eventBody = JsonSerializer.Serialize(new
        {
            eventId = "evt-integration-1",
            eventType = "conversation.started",
            providerSessionId = sessionId,
            conversationId = "97039a03-3777-4acd-810e-28c90013976d",
            stepIdx = 1,
            transcriptPath,
            timestamp = DateTimeOffset.UtcNow
        });

        using var eventRequest = BuildSignedInternalEventRequest(eventBody, "nonce-integration-1", _factory.TimeProvider.GetUtcNow());
        var eventResponse = await client.SendAsync(eventRequest);
        Assert.Equal(HttpStatusCode.OK, eventResponse.StatusCode);

        var detailResponse = await client.GetAsync($"/api/agent/provider-sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await ReadJsonAsync(detailResponse);
        Assert.Equal("97039a03-3777-4acd-810e-28c90013976d", detail.RootElement.GetProperty("conversationId").GetString());
        Assert.Equal(transcriptPath, detail.RootElement.GetProperty("lastTranscriptPath").GetString());
        Assert.Equal(new FileInfo(transcriptPath).Length, detail.RootElement.GetProperty("transcriptOffset").GetInt64());

        using var replayRequest = BuildSignedInternalEventRequest(eventBody, "nonce-integration-1", _factory.TimeProvider.GetUtcNow());
        var replayResponse = await client.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
    }

    [Fact]
    public async Task ChatCompletion_FallsBackToTranscriptDeltaWhenRuntimeStdoutIsEmpty()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAgyRuntimeManager>();
                services.AddSingleton<IAgyRuntimeManager, EmptyOutputRuntimeManager>();
            });
        });
        var client = factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync(factory, "provider-transcript-admin", password, isAdmin: true);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginCommand
        {
            Username = "provider-transcript-admin",
            Password = password
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Transcript Provider"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadJsonAsync(createResponse);
        var sessionId = created.RootElement.GetProperty("sessionId").GetString();
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        var transcriptPath = Path.Combine(Path.GetTempPath(), $"webterminal-transcript-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(transcriptPath, """{"role":"user","content":"first"}""" + Environment.NewLine);
        var eventBody = JsonSerializer.Serialize(new
        {
            eventId = "evt-transcript-fallback",
            eventType = "conversation.started",
            providerSessionId = sessionId,
            conversationId = "97039a03-3777-4acd-810e-28c90013976d",
            stepIdx = 1,
            transcriptPath,
            timestamp = DateTimeOffset.UtcNow
        });
        using var eventRequest = BuildSignedInternalEventRequest(eventBody, "nonce-transcript-fallback", _factory.TimeProvider.GetUtcNow());
        var eventResponse = await client.SendAsync(eventRequest);
        Assert.Equal(HttpStatusCode.OK, eventResponse.StatusCode);

        await File.AppendAllTextAsync(transcriptPath, """{"role":"assistant","content":"from transcript"}""" + Environment.NewLine);
        using var chatRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "reply" } },
                stream = false
            })
        };
        chatRequest.Headers.Authorization = new("Bearer", apiKey);

        var chatResponse = await client.SendAsync(chatRequest);
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);
        var chat = await ReadJsonAsync(chatResponse);
        Assert.Equal(
            "from transcript",
            chat.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
    }

    [Fact]
    public async Task ChatCompletion_AcceptsToolResultWhenWaitingForToolResult()
    {
        SequencedRuntimeManager.LastPrompt = null;
        SequencedRuntimeManager.Outputs.Clear();
        SequencedRuntimeManager.Outputs.Enqueue("""{"tool_calls":[{"id":"call_read","type":"function","function":{"name":"read_text_file","arguments":{"path":"README.md"}}}]}""");
        SequencedRuntimeManager.Outputs.Enqueue("tool result accepted");
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAgyRuntimeManager>();
                services.AddSingleton<IAgyRuntimeManager, SequencedRuntimeManager>();
            });
        });
        var client = factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync(factory, "provider-tool-admin", password, isAdmin: true);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginCommand
        {
            Username = "provider-tool-admin",
            Password = password
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Tool Provider"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadJsonAsync(createResponse);
        var sessionId = created.RootElement.GetProperty("sessionId").GetString();
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        using var firstChatRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "read README.md" } },
                tools = new[]
                {
                    new
                    {
                        type = "function",
                        function = new
                        {
                            name = "read_text_file",
                            parameters = new { type = "object" }
                        }
                    }
                },
                stream = false
            })
        };
        firstChatRequest.Headers.Authorization = new("Bearer", apiKey);
        var firstChatResponse = await client.SendAsync(firstChatRequest);
        Assert.Equal(HttpStatusCode.OK, firstChatResponse.StatusCode);
        var firstChat = await ReadJsonAsync(firstChatResponse);
        Assert.Equal(
            "tool_calls",
            firstChat.RootElement.GetProperty("choices")[0].GetProperty("finish_reason").GetString());

        using var secondChatRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[]
                {
                    new
                    {
                        role = "tool",
                        tool_call_id = "call_read",
                        name = "read_text_file",
                        content = "README content from harness"
                    }
                },
                stream = false
            })
        };
        secondChatRequest.Headers.Authorization = new("Bearer", apiKey);

        var chatResponse = await client.SendAsync(secondChatRequest);
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);
        var chat = await ReadJsonAsync(chatResponse);
        Assert.Equal(
            "tool result accepted",
            chat.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
        Assert.Contains("tool_call_id: call_read", SequencedRuntimeManager.LastPrompt);
        Assert.Contains("README content from harness", SequencedRuntimeManager.LastPrompt);
    }

    [Fact]
    public void ParseAgyChatOutput_ConvertsToolCallJsonToOpenAiToolCalls()
    {
        const string output = """
{"tool_calls":[{"id":"call_read","type":"function","function":{"name":"read_file","arguments":{"path":"README.md"}}}]}
""";

        var parsed = OpenAiCompatibleController.ParseAgyChatOutput(output);

        Assert.Null(parsed.Content);
        var toolCall = Assert.Single(parsed.ToolCalls);
        Assert.Equal("call_read", toolCall.Id);
        Assert.Equal("function", toolCall.Type);
        Assert.Equal("read_file", toolCall.Function.Name);
        Assert.Equal("""{"path":"README.md"}""", toolCall.Function.Arguments);
    }

    [Fact]
    public void ParseAgyChatOutput_KeepsPlainTextAsAssistantContent()
    {
        var parsed = OpenAiCompatibleController.ParseAgyChatOutput("plain answer");

        Assert.Equal("plain answer", parsed.Content);
        Assert.Empty(parsed.ToolCalls);
    }

    private async Task SeedUserAsync(string username, string plaintextPassword, bool isAdmin)
    {
        await SeedUserAsync(_factory, username, plaintextPassword, isAdmin);
    }

    private async Task SeedUserAsync(
        WebApplicationFactory<Program> factory,
        string username,
        string plaintextPassword,
        bool isAdmin)
    {
        using var scope = factory.Services.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<WebPowerShell.Application.Common.Interfaces.IUserRepository>();
        await userRepo.SaveAsync(new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = _passwordHasher.HashPassword(plaintextPassword),
            IsActive = true,
            IsAdmin = isAdmin,
            CreatedAt = _factory.TimeProvider.GetUtcNow(),
            UpdatedAt = _factory.TimeProvider.GetUtcNow(),
            LastPasswordChangeDate = _factory.TimeProvider.GetUtcNow(),
            FailedLoginCount = 0,
            LockedUntil = null
        });
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static HttpRequestMessage BuildSignedInternalEventRequest(string body, string nonce, DateTimeOffset now)
    {
        const string secret = "integration-test-agent-event-secret";
        var timestamp = now.ToUnixTimeSeconds().ToString();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{nonce}.{body}")))
            .ToLowerInvariant();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/agent-events")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Agent-Event-Timestamp", timestamp);
        request.Headers.Add("X-Agent-Event-Nonce", nonce);
        request.Headers.Add("X-Agent-Event-Signature", signature);
        return request;
    }

    private sealed class EmptyOutputRuntimeManager : IAgyRuntimeManager
    {
        public Task<ProviderSessionState> PrepareSessionAsync(
            ProviderSession session,
            CancellationToken cancellationToken = default)
        {
            session.State = ProviderSessionState.Ready;
            return Task.FromResult(session.State);
        }

        public Task<AgyCompletionResult> CompleteAsync(
            ProviderSession session,
            string prompt,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(AgyCompletionResult.Success(string.Empty));
        }
    }

    private sealed class SequencedRuntimeManager : IAgyRuntimeManager
    {
        public static Queue<string> Outputs { get; } = new();
        public static string? LastPrompt { get; set; }

        public Task<ProviderSessionState> PrepareSessionAsync(
            ProviderSession session,
            CancellationToken cancellationToken = default)
        {
            session.State = ProviderSessionState.Ready;
            return Task.FromResult(session.State);
        }

        public Task<AgyCompletionResult> CompleteAsync(
            ProviderSession session,
            string prompt,
            CancellationToken cancellationToken = default)
        {
            LastPrompt = prompt;
            var output = Outputs.Count > 0 ? Outputs.Dequeue() : string.Empty;
            return Task.FromResult(AgyCompletionResult.Success(output));
        }
    }
}
