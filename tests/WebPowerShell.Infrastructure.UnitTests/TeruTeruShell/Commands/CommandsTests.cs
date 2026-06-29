using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using WebPowerShell.Infrastructure.TeruTeruShell;
using WebPowerShell.Infrastructure.TeruTeruShell.Commands;

namespace WebPowerShell.Infrastructure.UnitTests.TeruTeruShell.Commands;

public class CommandsTests : IDisposable
{
    private readonly string _baseDir;
    private readonly IVirtualFileSystem _vfs;
    private readonly TestCommandContext _context;
    private readonly string _username = "cmd_tester";

    public CommandsTests()
    {
        _baseDir = Environment.CurrentDirectory;
        _vfs = new VirtualFileSystem(isAdmin: false, username: _username); // Use non-admin to isolate in home dir
        _context = new TestCommandContext(_vfs);
        
        var homeDir = Path.Combine(_baseDir, "home", _username);
        if (Directory.Exists(homeDir)) Directory.Delete(homeDir, true);
        Directory.CreateDirectory(homeDir);
        _vfs.ChangeDirectory("/");
    }

    public void Dispose()
    {
        var homeDir = Path.Combine(_baseDir, "home", _username);
        if (Directory.Exists(homeDir)) Directory.Delete(homeDir, true);
    }

    [Fact]
    public async Task DirCommand_ListsFiles()
    {
        var homeDir = Path.Combine(_baseDir, "home", _username);
        File.WriteAllText(Path.Combine(homeDir, "test.txt"), "hello");
        
        var cmd = new DirCommand();
        await cmd.ExecuteAsync(new string[0], _context);
        
        Assert.Contains("test.txt", _context.Outputs);
    }

    [Fact]
    public async Task LsCommand_ListsFiles()
    {
        var homeDir = Path.Combine(_baseDir, "home", _username);
        File.WriteAllText(Path.Combine(homeDir, "test.txt"), "hello");
        
        var cmd = new LsCommand();
        await cmd.ExecuteAsync(new string[0], _context);
        
        Assert.Contains("test.txt", _context.Outputs);
    }

    [Fact]
    public async Task MkdirCommand_CreatesDirectory()
    {
        var cmd = new MkdirCommand();
        await cmd.ExecuteAsync(new[] { "newdir" }, _context);
        
        var homeDir = Path.Combine(_baseDir, "home", _username);
        Assert.True(Directory.Exists(Path.Combine(homeDir, "newdir")));
    }

    [Fact]
    public async Task RmCommand_RemovesFile()
    {
        var homeDir = Path.Combine(_baseDir, "home", _username);
        var file = Path.Combine(homeDir, "remove.txt");
        File.WriteAllText(file, "hello");
        
        var cmd = new RmCommand();
        await cmd.ExecuteAsync(new[] { "remove.txt" }, _context);
        
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task CpCommand_CopiesFile()
    {
        var homeDir = Path.Combine(_baseDir, "home", _username);
        var src = Path.Combine(homeDir, "src.txt");
        var dest = Path.Combine(homeDir, "dest.txt");
        File.WriteAllText(src, "copy me");
        
        var cmd = new CpCommand();
        await cmd.ExecuteAsync(new[] { "src.txt", "dest.txt" }, _context);
        
        Assert.True(File.Exists(dest));
        Assert.Equal("copy me", File.ReadAllText(dest));
    }

    [Fact]
    public async Task PingCommand_FailsIfNoArgs()
    {
        var cmd = new PingCommand();
        await cmd.ExecuteAsync(new string[0], _context);
        
        Assert.Contains("ping: missing host operand", _context.Errors);
    }

    [Fact]
    public async Task AgyCommand_ExecutesGracefully()
    {
        var cmd = new AgyCommand();
        await cmd.ExecuteAsync(new[] { "--help" }, _context);
        // It either successfully runs (has outputs) or fails gracefully (has errors)
        Assert.True(_context.Outputs.Count > 0 || _context.Errors.Count > 0); 
    }
}
