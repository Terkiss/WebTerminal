using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using WebPowerShell.Domain.Common;
using WebPowerShell.Infrastructure.AgentRuntime;
using WebPowerShell.Infrastructure.ConPTY;

namespace WebPowerShell.Infrastructure.UnitTests;

public sealed class TerminalBackedAgyRuntimeManagerTests
{
    [Fact]
    public async Task CompleteAsync_UsesBoundTerminalSession()
    {
        var userId = Guid.NewGuid();
        var terminalId = Guid.NewGuid();
        var process = new EchoTerminalProcess();
        var terminalSession = new TerminalSession(
            terminalId,
            userId,
            process,
            NullLogger.Instance);
        terminalSession.Start(new TerminalLaunchOptions(
            "fake",
            "",
            "D:/workspace",
            null,
            80,
            24));

        var terminalManager = new FakeTerminalSessionManager(terminalSession);
        var manager = new AgyRuntimeManager(
            new AgyRuntimeProbe(NullLogger<AgyRuntimeProbe>.Instance),
            NullLogger<AgyRuntimeManager>.Instance,
            terminalManager,
            new EmptyConfiguration());
        var providerSession = new ProviderSession
        {
            OwnerUserId = userId,
            TerminalSessionId = terminalId,
            IsTerminalBacked = true,
            State = ProviderSessionState.Ready
        };

        var result = await manager.CompleteAsync(providerSession, "hello from api");

        Assert.True(result.IsSuccess);
        Assert.Contains("terminal-response: hello from api", result.Text);
        Assert.Equal(ProviderSessionState.WaitingForRequest, providerSession.State);
    }

    private sealed class EchoTerminalProcess : ITerminalProcess
    {
        private readonly Channel<ReadOnlyMemory<byte>> _output = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();

        public bool HasExited => false;
        public int? ExitCode => null;

        public Task StartAsync(TerminalLaunchOptions options, CancellationToken cancellationToken) => Task.CompletedTask;

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
        {
            var prompt = System.Text.Encoding.UTF8.GetString(input.Span).Trim();
            var output = System.Text.Encoding.UTF8.GetBytes($"terminal-response: {prompt}\r\n");
            await _output.Writer.WriteAsync(output, cancellationToken);
        }

        public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken) => Task.CompletedTask;

        public IAsyncEnumerable<ReadOnlyMemory<byte>> ReadOutputAsync(CancellationToken cancellationToken) =>
            _output.Reader.ReadAllAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            _output.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeTerminalSessionManager : ITerminalSessionManager
    {
        private readonly TerminalSession _session;

        public FakeTerminalSessionManager(TerminalSession session)
        {
            _session = session;
        }

        public Result<TerminalSession> GetSession(Guid sessionId) =>
            sessionId == _session.SessionId
                ? Result<TerminalSession>.Success(_session)
                : Result<TerminalSession>.Fail(AppFailure.SessionNotFound);

        public Task<Result<TerminalSession>> CreateSessionAsync(Guid userId, Guid sessionId, TerminalLaunchOptions options) =>
            Task.FromResult(Result<TerminalSession>.Fail(new AppFailure("Unsupported", "Not supported in this test.")));

        public Task<Result<bool>> CloseSessionAsync(Guid sessionId) =>
            Task.FromResult(Result<bool>.Success(true));

        public Task<Result<int>> CloseAllSessionsForUserAsync(Guid userId) =>
            Task.FromResult(Result<int>.Success(0));

        public IReadOnlyList<TerminalSession> GetAllSessions() => [_session];

        public IReadOnlyList<TerminalSession> GetSessionsForUser(Guid userId) =>
            _session.OwnerUserId == userId ? [_session] : [];

        public void StoreCallbackResult(Guid sessionId, string result)
        {
        }

        public string? RetrieveCallbackResult(Guid sessionId) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EmptyConfiguration : IConfiguration
    {
        public string? this[string key]
        {
            get => null;
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => new EmptyChangeToken();

        public IConfigurationSection GetSection(string key) => new EmptyConfigurationSection(key);
    }

    private sealed class EmptyConfigurationSection : IConfigurationSection
    {
        public EmptyConfigurationSection(string key)
        {
            Key = key;
            Path = key;
        }

        public string? this[string key]
        {
            get => null;
            set { }
        }

        public string Key { get; }
        public string Path { get; }
        public string? Value { get; set; }
        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => new EmptyChangeToken();
        public IConfigurationSection GetSection(string key) => new EmptyConfigurationSection(key);
    }

    private sealed class EmptyChangeToken : IChangeToken
    {
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => new NoopDisposable();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
