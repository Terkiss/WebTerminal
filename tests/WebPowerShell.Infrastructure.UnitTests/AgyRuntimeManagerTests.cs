using WebPowerShell.Infrastructure.AgentRuntime;

namespace WebPowerShell.Infrastructure.UnitTests;

public sealed class AgyRuntimeManagerTests
{
    [Fact]
    public void BuildArguments_IncludesConfiguredProviderRuntimeOptions()
    {
        var session = new ProviderSession
        {
            SessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            ConversationId = "11111111-2222-3333-4444-555555555555"
        };
        var options = new AgyRuntimeOptions
        {
            PrintTimeout = "7m",
            Agent = "coding-agent",
            Model = "agy-model",
            Mode = "accept-edits",
            Project = "project-1",
            WorkspacePaths = ["D:/External/One", "D:/External/Two"]
        };

        var args = AgyRuntimeManager.BuildArguments(session, "hello", "agy.log", options);

        Assert.Equal("--print", args[0]);
        Assert.Equal("hello", args[1]);
        Assert.Contains("--print-timeout", args);
        Assert.Contains("7m", args);
        Assert.Contains("--log-file", args);
        Assert.Contains("agy.log", args);
        Assert.Contains("--agent", args);
        Assert.Contains("coding-agent", args);
        Assert.Contains("--model", args);
        Assert.Contains("agy-model", args);
        Assert.Contains("--mode", args);
        Assert.Contains("accept-edits", args);
        Assert.Contains("--project", args);
        Assert.Contains("project-1", args);
        Assert.Equal(2, args.Count(argument => argument == "--add-dir"));
        Assert.Contains("D:/External/One", args);
        Assert.Contains("D:/External/Two", args);
        Assert.Contains("--conversation", args);
        Assert.Contains("11111111-2222-3333-4444-555555555555", args);
    }

    [Fact]
    public void BuildArguments_UsesDefaultTimeoutWhenConfiguredTimeoutIsBlank()
    {
        var session = new ProviderSession();
        var args = AgyRuntimeManager.BuildArguments(
            session,
            "hello",
            "agy.log",
            new AgyRuntimeOptions { PrintTimeout = " " });

        Assert.Contains("2m", args);
    }
}
