using WebPowerShell.Domain.Common;

namespace WebPowerShell.Infrastructure.TeruTeruShell;

/// <summary>
/// Provides a virtual file system interface for shell commands, enforcing security boundaries.
/// Sandbox rules ensure standard users cannot access files outside their home directory,
/// while admin users have unrestricted access.
/// </summary>
public interface IVirtualFileSystem
{
    string CurrentDirectory { get; }
    Result<bool> ChangeDirectory(string path);
    Result<string> ResolvePhysicalPath(string path);
}
