using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using WebPowerShell.Infrastructure.TeruTeruShell;
using WebPowerShell.Infrastructure.TeruTeruShell.Commands;
using WebPowerShell.Application.Common.Models;

namespace WebPowerShell.Infrastructure.UnitTests.TeruTeruShell.Commands;

public class TestCommandContext : ICommandContext
{
    public CancellationToken CancellationToken { get; } = CancellationToken.None;
    public IVirtualFileSystem FileSystem { get; set; }
    
    public List<string> Outputs { get; } = new();
    public List<string> Errors { get; } = new();
    
    public TestCommandContext(IVirtualFileSystem vfs)
    {
        FileSystem = vfs;
    }
    
    public Task WriteOutputAsync(ShellOutputPayload payload) => Task.CompletedTask;
    public Task WriteLineAsync(string text, string color = "")
    {
        Outputs.Add(text);
        return Task.CompletedTask;
    }
    public Task WriteErrorAsync(string text)
    {
        Errors.Add(text);
        return Task.CompletedTask;
    }
    public Task WriteSystemAsync(string text) => Task.CompletedTask;
}
