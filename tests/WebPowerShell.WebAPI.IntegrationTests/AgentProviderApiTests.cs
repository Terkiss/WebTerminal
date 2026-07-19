using System;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WebPowerShell.Application.Users.Commands.Login;
using WebPowerShell.Domain.Entities;
using WebPowerShell.Infrastructure.AgentRuntime;
using WebPowerShell.Infrastructure.Persistence;
using WebPowerShell.Infrastructure.Security;
using WebPowerShell.WebAPI.Controllers;

namespace WebPowerShell.WebAPI.IntegrationTests;

public sealed class AgentProviderApiTests : IClassFixture<TestWebApplicationFactory>
{
    private static int LoginIpCounter;

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

        var loginResponse = await LoginAsync(client, "provider-admin", password);
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
        Assert.Equal("agy", created.RootElement.GetProperty("harness").GetProperty("model").GetString());
        Assert.Equal(
            sessionId,
            created.RootElement.GetProperty("hookBridge").GetProperty("providerSessionId").GetString());
        Assert.Equal(
            "tools/agent-runtime/agy_hook_bridge.py",
            created.RootElement.GetProperty("hookBridge").GetProperty("scriptPath").GetString());
        Assert.True(
            created.RootElement
                .GetProperty("hookBridge")
                .GetProperty("agyHooks")
                .GetProperty("hooks")
                .TryGetProperty("PostInvocation", out _));
        Assert.Equal(
            apiKey,
            created.RootElement
                .GetProperty("connectionManifest")
                .GetProperty("openAi")
                .GetProperty("environment")
                .GetProperty("OPENAI_API_KEY")
                .GetString());
        Assert.Contains(
            apiKey!,
            created.RootElement
                .GetProperty("connectionManifest")
                .GetProperty("smokeTest")
                .GetProperty("command")
                .GetString());

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storedSession = await dbContext.ProviderSessions.FindAsync(Guid.Parse(sessionId!));
            Assert.NotNull(storedSession);
            Assert.Equal(ProviderSession.HashApiKey(apiKey!), storedSession.ApiKeyHash);
        }

        var restoredRegistry = new ProviderSessionRegistry(
            Substitute.For<ILogger<ProviderSessionRegistry>>(),
            _factory.Services.GetRequiredService<AgyRuntimeProbe>(),
            _factory.Services.GetRequiredService<IAgyRuntimeManager>(),
            _factory.Services.GetRequiredService<TranscriptDeltaReader>(),
            _factory.TimeProvider,
            _factory.Services.GetRequiredService<IProviderSessionStore>());
        var restoredSession = restoredRegistry.FindByApiKey(apiKey!);
        Assert.NotNull(restoredSession);
        Assert.Equal(Guid.Parse(sessionId!), restoredSession.SessionId);

        var detailResponse = await client.GetAsync($"/api/agent/provider-sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await ReadJsonAsync(detailResponse);
        Assert.False(detail.RootElement.TryGetProperty("apiKey", out _));
        Assert.Equal("https://localhost/v1", detail.RootElement.GetProperty("baseUrl").GetString());
        Assert.Equal("agy", detail.RootElement.GetProperty("harness").GetProperty("model").GetString());
        Assert.Equal(
            "https://localhost/api/internal/agent-events",
            detail.RootElement.GetProperty("hookBridge").GetProperty("endpoint").GetString());
        Assert.True(detail.RootElement.GetProperty("hookBridge").GetProperty("enabled").GetBoolean());
        Assert.Equal(
            "<apiKey>",
            detail.RootElement
                .GetProperty("connectionManifest")
                .GetProperty("openAi")
                .GetProperty("environment")
                .GetProperty("OPENAI_API_KEY")
                .GetString());
        Assert.DoesNotContain(
            apiKey!,
            detail.RootElement
                .GetProperty("connectionManifest")
                .GetProperty("smokeTest")
                .GetProperty("command")
                .GetString());

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
    public async Task ProviderSessionConfig_ShowsDisabledHookBridgeWhenSecretIsMissing()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AgentRuntime:InternalEventSecret"] = string.Empty
                });
            });
        });
        var client = factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync(factory, "provider-no-hook-secret-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-no-hook-secret-admin", password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "No Hook Secret Provider"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadJsonAsync(createResponse);
        Assert.False(created.RootElement.GetProperty("hookBridge").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task ProviderSessionExpiry_RevokesApiKeyAndRejectsHookEvents()
    {
        using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync(factory, "provider-expiry-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-expiry-admin", password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Expiring Provider"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadJsonAsync(createResponse);
        var sessionId = created.RootElement.GetProperty("sessionId").GetString();
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        factory.TimeProvider.Advance(TimeSpan.FromHours(9));

        using var modelsRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/models");
        modelsRequest.Headers.Authorization = new("Bearer", apiKey);
        var modelsResponse = await client.SendAsync(modelsRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, modelsResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/agent/provider-sessions");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await ReadJsonAsync(listResponse);
        Assert.Equal(0, list.RootElement.GetArrayLength());

        var eventBody = JsonSerializer.Serialize(new
        {
            eventId = "evt-expired-session",
            eventType = "conversation.started",
            providerSessionId = sessionId,
            conversationId = "97039a03-3777-4acd-810e-28c90013976d",
            stepIdx = 1,
            timestamp = factory.TimeProvider.GetUtcNow()
        });
        using var eventRequest = BuildSignedInternalEventRequest(eventBody, "nonce-expired-session", factory.TimeProvider.GetUtcNow());
        var eventResponse = await client.SendAsync(eventRequest);
        Assert.Equal(HttpStatusCode.Gone, eventResponse.StatusCode);
    }

    [Fact]
    public async Task CreateProviderSession_RequiresAdminRole()
    {
        var client = _factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync("provider-user", password, isAdmin: false);

        var loginResponse = await LoginAsync(client, "provider-user", password);
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

        var loginResponse = await LoginAsync(client, "provider-rate-admin", password);
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

        var loginResponse = await LoginAsync(client, "provider-events-admin", password);
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

        var auditLogs = await _factory.GetAuditLogsAsync();
        Assert.Contains(auditLogs, log =>
            log.Command == "agent.provider.event.conversation.started" &&
            log.SessionId == sessionId &&
            log.ResultStatus == "Success" &&
            log.CorrelationId == "evt-integration-1");
    }

    [Fact]
    public async Task InternalAgentEvents_AuditsRejectedSessionMapping()
    {
        using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        var missingSessionId = Guid.NewGuid();
        var eventBody = JsonSerializer.Serialize(new
        {
            eventId = "evt-missing-session",
            eventType = "invocation.completed",
            providerSessionId = missingSessionId,
            conversationId = "97039a03-3777-4acd-810e-28c90013976d",
            stepIdx = 2,
            timestamp = factory.TimeProvider.GetUtcNow()
        });

        using var eventRequest = BuildSignedInternalEventRequest(eventBody, "nonce-missing-session", factory.TimeProvider.GetUtcNow());
        var eventResponse = await client.SendAsync(eventRequest);

        Assert.Equal(HttpStatusCode.NotFound, eventResponse.StatusCode);
        var auditLogs = await factory.GetAuditLogsAsync();
        Assert.Contains(auditLogs, log =>
            log.Command == "agent.provider.event.invocation.completed" &&
            log.SessionId == missingSessionId.ToString() &&
            log.ResultStatus == "Rejected" &&
            log.ErrorCode == "SessionNotFound" &&
            log.CorrelationId == "evt-missing-session");
    }

    [Fact]
    public async Task InternalAgentEvents_RejectsPublicNetworkSource()
    {
        using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        var eventBody = JsonSerializer.Serialize(new
        {
            eventId = "evt-public-source",
            eventType = "invocation.completed",
            providerSessionId = Guid.NewGuid(),
            timestamp = factory.TimeProvider.GetUtcNow()
        });

        using var eventRequest = BuildSignedInternalEventRequest(
            eventBody,
            "nonce-public-source",
            factory.TimeProvider.GetUtcNow(),
            forwardedFor: "8.8.8.8");
        var eventResponse = await client.SendAsync(eventRequest);

        Assert.Equal(HttpStatusCode.Forbidden, eventResponse.StatusCode);
    }

    [Fact]
    public async Task InternalAgentEvents_AllowsPrivateNetworkSource()
    {
        using var factory = new TestWebApplicationFactory();
        var client = factory.CreateClient();
        var missingSessionId = Guid.NewGuid();
        var eventBody = JsonSerializer.Serialize(new
        {
            eventId = "evt-private-source",
            eventType = "invocation.completed",
            providerSessionId = missingSessionId,
            timestamp = factory.TimeProvider.GetUtcNow()
        });

        using var eventRequest = BuildSignedInternalEventRequest(
            eventBody,
            "nonce-private-source",
            factory.TimeProvider.GetUtcNow(),
            forwardedFor: "192.168.1.25");
        var eventResponse = await client.SendAsync(eventRequest);

        Assert.Equal(HttpStatusCode.NotFound, eventResponse.StatusCode);
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

        var loginResponse = await LoginAsync(client, "provider-transcript-admin", password);
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
    public async Task ChatCompletion_ExtractsNestedTranscriptContentParts()
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
        await SeedUserAsync(factory, "provider-nested-transcript-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-nested-transcript-admin", password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Nested Transcript Provider"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadJsonAsync(createResponse);
        var sessionId = created.RootElement.GetProperty("sessionId").GetString();
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        var transcriptPath = Path.Combine(Path.GetTempPath(), $"webterminal-transcript-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(transcriptPath, """{"message":{"role":"user","content":"first"}}""" + Environment.NewLine);
        var eventBody = JsonSerializer.Serialize(new
        {
            eventId = "evt-nested-transcript",
            eventType = "conversation.started",
            providerSessionId = sessionId,
            conversationId = "97039a03-3777-4acd-810e-28c90013976d",
            stepIdx = 1,
            transcriptPath,
            timestamp = DateTimeOffset.UtcNow
        });
        using var eventRequest = BuildSignedInternalEventRequest(eventBody, "nonce-nested-transcript", _factory.TimeProvider.GetUtcNow());
        var eventResponse = await client.SendAsync(eventRequest);
        Assert.Equal(HttpStatusCode.OK, eventResponse.StatusCode);

        await File.AppendAllTextAsync(
            transcriptPath,
            """{"message":{"role":"assistant","content":[{"type":"text","text":"nested"},{"type":"text","text":"answer"}]}}""" + Environment.NewLine);
        using var chatRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "reply" } }
            })
        };
        chatRequest.Headers.Authorization = new("Bearer", apiKey);

        var chatResponse = await client.SendAsync(chatRequest);

        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);
        var chat = await ReadJsonAsync(chatResponse);
        Assert.Equal(
            $"nested{Environment.NewLine}answer",
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

        var loginResponse = await LoginAsync(client, "provider-tool-admin", password);
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
                messages = new object[]
                {
                    new { role = "user", content = "read README.md" },
                    new
                    {
                        role = "assistant",
                        content = (string?)null,
                        tool_calls = new[]
                        {
                            new
                            {
                                id = "call_read",
                                type = "function",
                                function = new
                                {
                                    name = "read_text_file",
                                    arguments = """{"path":"README.md"}"""
                                }
                            }
                        }
                    },
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
        Assert.Contains("Original tool call:", SequencedRuntimeManager.LastPrompt);
        Assert.Contains("read_text_file", SequencedRuntimeManager.LastPrompt);
        Assert.Contains("README content from harness", SequencedRuntimeManager.LastPrompt);
    }

    [Fact]
    public async Task ChatCompletion_StreamsToolCallsWithOpenAiDeltaShape()
    {
        SequencedRuntimeManager.Outputs.Clear();
        SequencedRuntimeManager.Outputs.Enqueue("""{"tool_calls":[{"id":"call_stream","type":"function","function":{"name":"read_text_file","arguments":{"path":"README.md"}}}]}""");
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
        await SeedUserAsync(factory, "provider-stream-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-stream-admin", password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Stream Provider"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var streamRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "stream tool" } },
                stream = true,
                tools = new[] { new { type = "function", function = new { name = "read_text_file" } } }
            })
        };
        streamRequest.Headers.Authorization = new("Bearer", apiKey);

        var streamResponse = await client.SendAsync(streamRequest);

        Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
        Assert.Equal("text/event-stream", streamResponse.Content.Headers.ContentType?.MediaType);
        var sse = await streamResponse.Content.ReadAsStringAsync();
        Assert.Contains("data: [DONE]", sse);
        Assert.Contains(@"""finish_reason"":""tool_calls""", sse);
        Assert.Contains(@"""tool_calls"":[{""index"":0,""id"":""call_stream""", sse);
    }

    [Fact]
    public async Task ChatCompletion_AcceptsOpenAiContentParts()
    {
        SequencedRuntimeManager.LastPrompt = null;
        SequencedRuntimeManager.Outputs.Clear();
        SequencedRuntimeManager.Outputs.Enqueue("content parts accepted");
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
        await SeedUserAsync(factory, "provider-content-parts-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-content-parts-admin", password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Content Parts Provider"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var chatRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = "first part" },
                            new { type = "text", text = "second part" }
                        }
                    }
                }
            })
        };
        chatRequest.Headers.Authorization = new("Bearer", apiKey);

        var chatResponse = await client.SendAsync(chatRequest);
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);
        var chat = await ReadJsonAsync(chatResponse);
        Assert.Equal(
            "content parts accepted",
            chat.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
        Assert.Contains("first part", SequencedRuntimeManager.LastPrompt);
        Assert.Contains("second part", SequencedRuntimeManager.LastPrompt);
    }

    [Fact]
    public async Task ChatCompletion_RespectsToolChoiceNone()
    {
        SequencedRuntimeManager.LastPrompt = null;
        SequencedRuntimeManager.Outputs.Clear();
        SequencedRuntimeManager.Outputs.Enqueue("no tools");
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
        await SeedUserAsync(factory, "provider-tool-choice-none-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-tool-choice-none-admin", password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Tool Choice None Provider"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var chatRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "answer directly" } },
                tools = new[] { new { type = "function", function = new { name = "read_text_file" } } },
                tool_choice = "none"
            })
        };
        chatRequest.Headers.Authorization = new("Bearer", apiKey);

        var chatResponse = await client.SendAsync(chatRequest);

        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);
        Assert.Equal("answer directly", SequencedRuntimeManager.LastPrompt);
    }

    [Fact]
    public async Task ChatCompletion_IncludesForcedToolChoiceInstruction()
    {
        SequencedRuntimeManager.LastPrompt = null;
        SequencedRuntimeManager.Outputs.Clear();
        SequencedRuntimeManager.Outputs.Enqueue("forced tool prompt accepted");
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
        await SeedUserAsync(factory, "provider-tool-choice-forced-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-tool-choice-forced-admin", password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Forced Tool Provider"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var chatRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "use chosen tool" } },
                tools = new[] { new { type = "function", function = new { name = "read_text_file" } } },
                tool_choice = new
                {
                    type = "function",
                    function = new { name = "read_text_file" }
                }
            })
        };
        chatRequest.Headers.Authorization = new("Bearer", apiKey);

        var chatResponse = await client.SendAsync(chatRequest);

        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);
        Assert.Contains("Tool choice: call only the function named read_text_file.", SequencedRuntimeManager.LastPrompt);
    }

    [Fact]
    public async Task ChatCompletion_RejectsDuplicateIdempotencyKey()
    {
        CountingRuntimeManager.CallCount = 0;
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAgyRuntimeManager>();
                services.AddSingleton<IAgyRuntimeManager, CountingRuntimeManager>();
            });
        });
        var client = factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync(factory, "provider-idempotency-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-idempotency-admin", password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Idempotency Provider"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var firstChatRequest = BuildChatRequest(apiKey, "idempotency-test-key");
        var firstChatResponse = await client.SendAsync(firstChatRequest);
        Assert.Equal(HttpStatusCode.OK, firstChatResponse.StatusCode);

        using var duplicateChatRequest = BuildChatRequest(apiKey, "idempotency-test-key");
        var duplicateChatResponse = await client.SendAsync(duplicateChatRequest);
        Assert.Equal(HttpStatusCode.Conflict, duplicateChatResponse.StatusCode);
        Assert.Equal(1, CountingRuntimeManager.CallCount);
    }

    [Fact]
    public async Task ChatCompletion_ReturnsGatewayTimeoutWhenRuntimeTimesOut()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AgentRuntime:RequestTimeoutSeconds"] = "1"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAgyRuntimeManager>();
                services.AddSingleton<IAgyRuntimeManager, SlowRuntimeManager>();
            });
        });
        var client = factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync(factory, "provider-timeout-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-timeout-admin", password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Timeout Provider"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var chatRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "wait" } }
            })
        };
        chatRequest.Headers.Authorization = new("Bearer", apiKey);

        var chatResponse = await client.SendAsync(chatRequest);

        Assert.Equal(HttpStatusCode.GatewayTimeout, chatResponse.StatusCode);
        var body = await ReadJsonAsync(chatResponse);
        Assert.Equal(
            "provider_timeout",
            body.RootElement.GetProperty("error").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ChatCompletion_RejectsRequestsBeforeProviderIsReady()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAgyRuntimeManager>();
                services.AddSingleton<IAgyRuntimeManager, StartingRuntimeManager>();
            });
        });
        var client = factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync(factory, "provider-starting-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-starting-admin", password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Starting Provider"
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();
        Assert.Equal("Starting", created.RootElement.GetProperty("state").GetString());

        using var chatRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "too soon" } }
            })
        };
        chatRequest.Headers.Authorization = new("Bearer", apiKey);

        var chatResponse = await client.SendAsync(chatRequest);

        Assert.Equal(HttpStatusCode.Conflict, chatResponse.StatusCode);
        var body = await ReadJsonAsync(chatResponse);
        Assert.Equal(
            "provider_not_ready",
            body.RootElement.GetProperty("error").GetProperty("type").GetString());
        Assert.Equal(0, StartingRuntimeManager.CallCount);
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

    [Fact]
    public async Task TranscriptDeltaReader_ExtractsNestedToolCalls()
    {
        var transcriptPath = Path.Combine(Path.GetTempPath(), $"webterminal-transcript-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(
            transcriptPath,
            """{"message":{"role":"assistant","tool_calls":[{"id":"call_nested","type":"function","function":{"name":"read_text_file","arguments":"{}"}}]}}""" + Environment.NewLine);
        var session = new ProviderSession
        {
            LastTranscriptPath = transcriptPath
        };
        var reader = new TranscriptDeltaReader();

        var delta = await reader.ReadDeltaAsync(session);

        var entry = Assert.Single(delta.Entries);
        Assert.Equal("assistant", entry.Role);
        Assert.Contains("call_nested", entry.Content);
    }

    private async Task SeedUserAsync(string username, string plaintextPassword, bool isAdmin)
    {
        await SeedUserAsync(_factory, username, plaintextPassword, isAdmin);
    }

    [Fact]
    public async Task ResponsesApi_ReturnsTextResponse()
    {
        SequencedRuntimeManager.Outputs.Clear();
        SequencedRuntimeManager.Outputs.Enqueue("responses text");
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
        await SeedUserAsync(factory, "provider-resp-text-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-resp-text-admin", password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Resp Text Provider"
        });
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                input = "hello responses",
                stream = false
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);

        Assert.Equal("response", json.RootElement.GetProperty("object").GetString());
        Assert.Equal("completed", json.RootElement.GetProperty("status").GetString());
        var output = json.RootElement.GetProperty("output");
        Assert.Equal(1, output.GetArrayLength());
        Assert.Equal("message", output[0].GetProperty("type").GetString());
        Assert.Equal("assistant", output[0].GetProperty("role").GetString());
        Assert.Equal("responses text", output[0].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task ResponsesApi_ReturnsFunctionCallOutputItem()
    {
        SequencedRuntimeManager.Outputs.Clear();
        SequencedRuntimeManager.Outputs.Enqueue("{\"tool_calls\":[{\"id\":\"call_resp\",\"type\":\"function\",\"function\":{\"name\":\"read_text_file\",\"arguments\":\"{\\\"path\\\":\\\"README.md\\\"}\"}}]}");
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
        await SeedUserAsync(factory, "provider-resp-func-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-resp-func-admin", password);
        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Resp Func Provider"
        });
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                input = "read file",
                stream = false
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);

        Assert.Equal("requires_action", json.RootElement.GetProperty("status").GetString());
        var output = json.RootElement.GetProperty("output");
        Assert.Equal(1, output.GetArrayLength());
        Assert.Equal("function_call", output[0].GetProperty("type").GetString());
        Assert.Equal("read_text_file", output[0].GetProperty("name").GetString());
        Assert.Equal("{\"path\":\"README.md\"}", output[0].GetProperty("arguments").GetString());
        Assert.Equal("call_resp", output[0].GetProperty("call_id").GetString());
    }

    [Fact]
    public async Task ResponsesApi_RejectsUnsupportedModel()
    {
        var client = _factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync("provider-resp-model-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-resp-model-admin", password);
        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Resp Model Provider"
        });
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new
            {
                model = "gpt-4",
                input = "test"
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResponsesApi_RequiresProviderReady()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAgyRuntimeManager>();
                services.AddSingleton<IAgyRuntimeManager, StartingRuntimeManager>();
            });
        });
        var client = factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync(factory, "provider-resp-start-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-resp-start-admin", password);
        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Resp Start Provider"
        });
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                input = "too soon"
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ResponsesApi_StreamsTextDeltas()
    {
        SequencedRuntimeManager.Outputs.Clear();
        SequencedRuntimeManager.Outputs.Enqueue("streamed text");
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
        await SeedUserAsync(factory, "provider-resp-stream-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-resp-stream-admin", password);
        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Resp Stream Provider"
        });
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                input = "stream this",
                stream = true
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sse = await response.Content.ReadAsStringAsync();
        Assert.Contains("data: [DONE]", sse);
        Assert.Contains("\"type\":\"response.text.delta\"", sse);
        Assert.Contains("\"delta\":\"streamed text\"", sse);
    }

    [Fact]
    public async Task ChatCompletion_RejectsUnsupportedN()
    {
        var client = _factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync("provider-chat-n-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-chat-n-admin", password);
        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Chat N Provider"
        });
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "hello" } },
                n = 2
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChatCompletion_AcceptsParallelToolCallsFlag()
    {
        SequencedRuntimeManager.Outputs.Clear();
        SequencedRuntimeManager.Outputs.Enqueue("parallel accepted");
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
        await SeedUserAsync(factory, "provider-chat-parallel-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-chat-parallel-admin", password);
        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Chat Parallel Provider"
        });
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "parallel tools" } },
                parallel_tool_calls = false
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginCommand
            {
                Username = username,
                Password = password
            })
        };

        var host = System.Threading.Interlocked.Increment(ref LoginIpCounter);
        request.Headers.Add("X-Forwarded-For", $"10.252.{host / 250}.{host % 250 + 1}");
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage BuildSignedInternalEventRequest(
        string body,
        string nonce,
        DateTimeOffset now,
        string? forwardedFor = "127.0.0.1")
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
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return request;
    }

    private static HttpRequestMessage BuildChatRequest(string? apiKey, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "hello" } }
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private async Task<(HttpClient Client, string ApiKey, ProviderSession Session, ProviderSessionRegistry Registry)> CreateTestSessionAsync(string testName)
    {
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
        await SeedUserAsync(factory, "provider-" + testName + "-user", password, isAdmin: true);
        await LoginAsync(client, "provider-" + testName + "-user", password);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Test Session"
        });
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString()!;
        var sessionId = Guid.Parse(created.RootElement.GetProperty("sessionId").GetString()!);

        var registry = factory.Services.GetRequiredService<ProviderSessionRegistry>();
        var session = registry.GetById(sessionId)!;
        return (client, apiKey, session, registry);
    }

    [Fact]
    public async Task ChatCompletion_RejectsUnknownToolCallId()
    {
        var (client, apiKey, session, registry) = await CreateTestSessionAsync("chat-unknown");
        session.State = ProviderSessionState.WaitingForToolResult;
        session.ExpectedToolCallIds.Add("call_123");
        registry.Save(session);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new object[]
                {
                    new { role = "user", content = "do something" },
                    new { role = "assistant", tool_calls = new[] { new { id = "call_123", type = "function", function = new { name = "my_tool", arguments = "{}" } } } },
                    new { role = "tool", tool_call_id = "unknown_call", name = "my_tool", content = "result" }
                }
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("invalid_request_error", body.RootElement.GetProperty("error").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ChatCompletion_RejectsDuplicateToolResult()
    {
        var (client, apiKey, session, registry) = await CreateTestSessionAsync("chat-dup");
        session.State = ProviderSessionState.WaitingForToolResult;
        session.ExpectedToolCallIds.Add("call_123");
        registry.Save(session);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new object[]
                {
                    new { role = "user", content = "do something" },
                    new { role = "assistant", tool_calls = new[] { new { id = "call_123", type = "function", function = new { name = "my_tool", arguments = "{}" } } } },
                    new { role = "tool", tool_call_id = "call_123", name = "my_tool", content = "result" },
                    new { role = "tool", tool_call_id = "call_123", name = "my_tool", content = "duplicate result" }
                }
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChatCompletion_PreservesPendingToolCallsWhenPartialResultsRejected()
    {
        var (client, apiKey, session, registry) = await CreateTestSessionAsync("chat-partial");
        session.State = ProviderSessionState.WaitingForToolResult;
        session.ExpectedToolCallIds.Add("call_one");
        session.ExpectedToolCallIds.Add("call_two");
        registry.Save(session);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new object[]
                {
                    new { role = "user", content = "do two things" },
                    new
                    {
                        role = "assistant",
                        tool_calls = new[]
                        {
                            new { id = "call_one", type = "function", function = new { name = "my_tool", arguments = "{}" } },
                            new { id = "call_two", type = "function", function = new { name = "my_tool", arguments = "{}" } }
                        }
                    },
                    new { role = "tool", tool_call_id = "call_one", name = "my_tool", content = "partial result" }
                }
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("call_one", session.ExpectedToolCallIds);
        Assert.Contains("call_two", session.ExpectedToolCallIds);
    }

    [Fact]
    public async Task ResponsesApi_RejectsUnknownFunctionCallOutputId()
    {
        var (client, apiKey, session, registry) = await CreateTestSessionAsync("resp-unknown");
        session.State = ProviderSessionState.WaitingForToolResult;
        session.ExpectedToolCallIds.Add("call_123");
        registry.Save(session);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                input = new[] { new { type = "function_call_output", call_id = "unknown", output = "result" } }
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResponsesApi_RejectsDuplicateFunctionCallOutput()
    {
        var (client, apiKey, session, registry) = await CreateTestSessionAsync("resp-dup");
        session.State = ProviderSessionState.WaitingForToolResult;
        registry.Save(session);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                input = new[] { new { type = "function_call_output", call_id = "call_123", output = "result" } }
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResponsesApi_AllowsKnownFunctionCallOutput()
    {
        var (client, apiKey, session, registry) = await CreateTestSessionAsync("resp-known");
        session.State = ProviderSessionState.WaitingForToolResult;
        session.ExpectedToolCallIds.Add("call_123");
        registry.Save(session);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                input = new[] { new { type = "function_call_output", call_id = "call_123", output = "result" } }
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ResponsesApi_PreservesPendingToolCallsWhenPartialOutputRejected()
    {
        var (client, apiKey, session, registry) = await CreateTestSessionAsync("resp-partial");
        session.State = ProviderSessionState.WaitingForToolResult;
        session.ExpectedToolCallIds.Add("call_one");
        session.ExpectedToolCallIds.Add("call_two");
        registry.Save(session);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                input = new[] { new { type = "function_call_output", call_id = "call_one", output = "partial result" } }
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("call_one", session.ExpectedToolCallIds);
        Assert.Contains("call_two", session.ExpectedToolCallIds);
    }

    [Fact]
    public async Task ResponsesApi_AcceptsKnownPreviousResponseId()
    {
        var (client, apiKey, session, registry) = await CreateTestSessionAsync("resp-prev-ok");
        session.State = ProviderSessionState.Ready;
        session.LastResponseId = "resp_known123";
        registry.Save(session);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                previous_response_id = "resp_known123",
                input = "hello"
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ResponsesApi_RejectsUnknownPreviousResponseId()
    {
        var (client, apiKey, session, registry) = await CreateTestSessionAsync("resp-prev-fail");
        session.State = ProviderSessionState.Ready;
        session.LastResponseId = "resp_known123";
        registry.Save(session);

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                previous_response_id = "resp_unknown999",
                input = "hello"
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    private sealed class CountingRuntimeManager : IAgyRuntimeManager
    {
        public static int CallCount { get; set; }

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
            CallCount++;
            return Task.FromResult(AgyCompletionResult.Success("counted"));
        }
    }

    private sealed class SlowRuntimeManager : IAgyRuntimeManager
    {
        public Task<ProviderSessionState> PrepareSessionAsync(
            ProviderSession session,
            CancellationToken cancellationToken = default)
        {
            session.State = ProviderSessionState.Ready;
            return Task.FromResult(session.State);
        }

        public async Task<AgyCompletionResult> CompleteAsync(
            ProviderSession session,
            string prompt,
            CancellationToken cancellationToken = default)
        {
            session.State = ProviderSessionState.Generating;
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            return AgyCompletionResult.Success("too late");
        }
    }

    private sealed class StartingRuntimeManager : IAgyRuntimeManager
    {
        public static int CallCount { get; set; }

        public Task<ProviderSessionState> PrepareSessionAsync(
            ProviderSession session,
            CancellationToken cancellationToken = default)
        {
            CallCount = 0;
            session.State = ProviderSessionState.Starting;
            return Task.FromResult(session.State);
        }

        public Task<AgyCompletionResult> CompleteAsync(
            ProviderSession session,
            string prompt,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(AgyCompletionResult.Success("should not run"));
        }
    }

    [Fact]
    public async Task ResponsesApi_StreamsOpenAiCompatibleTextEventSequence()
    {
        SequencedRuntimeManager.Outputs.Clear();
        SequencedRuntimeManager.Outputs.Enqueue("test response content");
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
        await SeedUserAsync(factory, "provider-resp-text-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-resp-text-admin", password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Resp Text Provider"
        });
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var streamRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                input = "stream text please",
                stream = true
            })
        };
        streamRequest.Headers.Authorization = new("Bearer", apiKey);

        var streamResponse = await client.SendAsync(streamRequest);
        Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
        var sse = await streamResponse.Content.ReadAsStringAsync();

        Assert.Contains("response.created", sse);
        Assert.Contains("response.in_progress", sse);
        Assert.Contains("response.output_item.added", sse);
        Assert.Contains("response.content_part.added", sse);
        Assert.Contains("response.text.delta", sse);
        Assert.Contains("response.content_part.done", sse);
        Assert.Contains("response.output_item.done", sse);
        Assert.Contains("response.done", sse);
        Assert.Contains("data: [DONE]", sse);
    }

    [Fact]
    public async Task ResponsesApi_StreamsOpenAiCompatibleFunctionCallEventSequence()
    {
        SequencedRuntimeManager.Outputs.Clear();
        SequencedRuntimeManager.Outputs.Enqueue("""{"tool_calls":[{"id":"call_xyz","type":"function","function":{"name":"test_tool","arguments":"{\"prop\":\"val\"}"}}]}""");
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
        await SeedUserAsync(factory, "provider-resp-tool-admin", password, isAdmin: true);

        var loginResponse = await LoginAsync(client, "provider-resp-tool-admin", password);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Resp Tool Provider"
        });
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var streamRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/responses")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                input = "stream tool please",
                stream = true,
                tools = new[] { new { type = "function", function = new { name = "test_tool" } } }
            })
        };
        streamRequest.Headers.Authorization = new("Bearer", apiKey);

        var streamResponse = await client.SendAsync(streamRequest);
        Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
        var sse = await streamResponse.Content.ReadAsStringAsync();

        Assert.Contains("response.created", sse);
        Assert.Contains("response.in_progress", sse);
        Assert.Contains("response.output_item.added", sse);
        Assert.Contains("response.function_call_arguments.delta", sse);
        Assert.Contains("response.function_call_arguments.done", sse);
        Assert.Contains("response.output_item.done", sse);
        Assert.Contains("response.done", sse);
        Assert.Contains("data: [DONE]", sse);
    }

    [Fact]
    public async Task ChatCompletion_StreamsOpenAiCompatibleTextSequence()
    {
        SequencedRuntimeManager.Outputs.Clear();
        SequencedRuntimeManager.Outputs.Enqueue("test chat text content");
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
        await SeedUserAsync(factory, "provider-chat-text-admin", password, isAdmin: true);

        await LoginAsync(client, "provider-chat-text-admin", password);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Chat Text Provider"
        });
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var streamRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "hello text" } },
                stream = true
            })
        };
        streamRequest.Headers.Authorization = new("Bearer", apiKey);

        var streamResponse = await client.SendAsync(streamRequest);
        Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
        var sse = await streamResponse.Content.ReadAsStringAsync();

        Assert.Contains("data: [DONE]", sse);
        Assert.Contains("\"role\":\"assistant\"", sse);
        Assert.Contains("test chat text content", sse);
        Assert.Contains("\"finish_reason\":\"stop\"", sse);
    }

    [Fact]
    public async Task ChatCompletion_StreamsMultipleToolCallDeltas()
    {
        SequencedRuntimeManager.Outputs.Clear();
        SequencedRuntimeManager.Outputs.Enqueue("{\"tool_calls\":[{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"t1\",\"arguments\":\"{}\"}},{\"id\":\"call_2\",\"type\":\"function\",\"function\":{\"name\":\"t2\",\"arguments\":\"{}\"}}]}");
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
        await SeedUserAsync(factory, "provider-chat-multitool-admin", password, isAdmin: true);

        await LoginAsync(client, "provider-chat-multitool-admin", password);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Chat MultiTool Provider"
        });
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var streamRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "do tools" } },
                stream = true,
                tools = new[] { new { type = "function", function = new { name = "t1" } } }
            })
        };
        streamRequest.Headers.Authorization = new("Bearer", apiKey);

        var streamResponse = await client.SendAsync(streamRequest);
        Assert.Equal(HttpStatusCode.OK, streamResponse.StatusCode);
        var sse = await streamResponse.Content.ReadAsStringAsync();

        Assert.Contains("data: [DONE]", sse);
        Assert.Contains("\"finish_reason\":\"tool_calls\"", sse);
        Assert.Contains("\"index\":0", sse);
        Assert.Contains("\"index\":1", sse);
        Assert.Contains("call_1", sse);
        Assert.Contains("call_2", sse);
    }

    [Fact]
    public async Task ChatCompletion_StreamingEndsWithDone()
    {
        // Actually this is implicitly tested in the above tests that assert "data: [DONE]", 
        // but we'll add a specific assertion that it's at the very end.
        SequencedRuntimeManager.Outputs.Clear();
        SequencedRuntimeManager.Outputs.Enqueue("test content");
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
        await SeedUserAsync(factory, "provider-chat-done-admin", password, isAdmin: true);

        await LoginAsync(client, "provider-chat-done-admin", password);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Chat Done Provider"
        });
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var streamRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "hello" } },
                stream = true
            })
        };
        streamRequest.Headers.Authorization = new("Bearer", apiKey);

        var streamResponse = await client.SendAsync(streamRequest);
        var sse = await streamResponse.Content.ReadAsStringAsync();

        Assert.EndsWith("data: [DONE]\n\n", sse);
    }

    [Fact]
    public async Task ChatCompletion_RejectsUnsupportedParameters()
    {
        var client = _factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync("provider-policy-admin", password, isAdmin: true);

        await LoginAsync(client, "provider-policy-admin", password);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Policy Provider"
        });
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var chatRequest1 = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "hi" } },
                logprobs = true
            })
        };
        chatRequest1.Headers.Authorization = new("Bearer", apiKey);

        var response1 = await client.SendAsync(chatRequest1);
        Assert.Equal(HttpStatusCode.BadRequest, response1.StatusCode);
        var err1 = await ReadJsonAsync(response1);
        Assert.Equal("invalid_request_error", err1.RootElement.GetProperty("error").GetProperty("type").GetString());

        using var chatRequest2 = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                messages = new[] { new { role = "user", content = "hi" } },
                response_format = new { type = "json_object" }
            })
        };
        chatRequest2.Headers.Authorization = new("Bearer", apiKey);

        var response2 = await client.SendAsync(chatRequest2);
        Assert.Equal(HttpStatusCode.BadRequest, response2.StatusCode);
        var err2 = await ReadJsonAsync(response2);
        Assert.Equal("invalid_request_error", err2.RootElement.GetProperty("error").GetProperty("type").GetString());
    }

    [Fact]
    public async Task UnsupportedApi_ReturnsInvalidRequestError()
    {
        var client = _factory.CreateClient();
        const string password = "CorrectPassword123!";
        await SeedUserAsync("provider-unsupported-api-admin", password, isAdmin: true);

        await LoginAsync(client, "provider-unsupported-api-admin", password);

        var createResponse = await client.PostAsJsonAsync("/api/agent/provider-sessions", new
        {
            profile = "agy-default",
            displayName = "Unsupported API Provider"
        });
        var created = await ReadJsonAsync(createResponse);
        var apiKey = created.RootElement.GetProperty("apiKey").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/embeddings")
        {
            Content = JsonContent.Create(new
            {
                model = "agy",
                input = "hello"
            })
        };
        request.Headers.Authorization = new("Bearer", apiKey);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var err = await ReadJsonAsync(response);
        Assert.Equal("invalid_request_error", err.RootElement.GetProperty("error").GetProperty("type").GetString());
        Assert.Contains("Unsupported API endpoint", err.RootElement.GetProperty("error").GetProperty("message").GetString());
    }
}
