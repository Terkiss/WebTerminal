using System;
using System.IO;
using WebPowerShell.Domain.Common;

namespace WebPowerShell.Infrastructure.TeruTeruShell;

/// <summary>
/// Implementation of IVirtualFileSystem that enforces sandbox rules for non-admin users.
/// Admin users have full physical access to the system.
/// </summary>
public class VirtualFileSystem : IVirtualFileSystem
{
    private readonly bool _isAdmin;
    private readonly string? _homePhysicalPath;
    private string _currentDirectory;

    public string CurrentDirectory => _currentDirectory;

    public VirtualFileSystem(bool isAdmin, string username)
    {
        _isAdmin = isAdmin;
        if (_isAdmin)
        {
            _currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(_currentDirectory))
            {
                _currentDirectory = Path.GetPathRoot(Environment.CurrentDirectory) ?? "C:\\";
            }
        }
        else
        {
            var baseDir = Environment.CurrentDirectory;
            _homePhysicalPath = Path.GetFullPath(Path.Combine(baseDir, "home", username));
            if (!Directory.Exists(_homePhysicalPath))
            {
                Directory.CreateDirectory(_homePhysicalPath);
            }
            _currentDirectory = "/";
        }
    }

    public Result<bool> ChangeDirectory(string path)
    {
        var resolvedResult = ResolvePhysicalPath(path);
        if (!resolvedResult.IsSuccess)
        {
            return Result<bool>.Fail(resolvedResult.Failure!);
        }

        var physicalPath = resolvedResult.Value!;
        if (!Directory.Exists(physicalPath))
        {
            return Result<bool>.Fail(AppFailure.Forbidden with { Message = "Directory not found" }); // We'll just reuse Forbidden but change message if needed, or better, we can add PathNotFound to AppFailure later. Let's add it to AppFailure.
        }

        if (_isAdmin)
        {
            _currentDirectory = physicalPath;
        }
        else
        {
            // Calculate virtual path
            if (physicalPath.Equals(_homePhysicalPath!, StringComparison.OrdinalIgnoreCase))
            {
                _currentDirectory = "/";
            }
            else if (physicalPath.StartsWith(_homePhysicalPath! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                     physicalPath.StartsWith(_homePhysicalPath! + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var relative = physicalPath.Substring(_homePhysicalPath!.Length).Replace('\\', '/');
                _currentDirectory = relative;
            }
            else
            {
                return Result<bool>.Fail(AppFailure.Unauthorized);
            }
        }
        return Result<bool>.Success(true);
    }

    public Result<string> ResolvePhysicalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<string>.Success(_isAdmin ? _currentDirectory : _homePhysicalPath!);
        }

        if (_isAdmin)
        {
            try
            {
                string combined = Path.IsPathRooted(path) ? path : Path.Combine(_currentDirectory, path);
                string fullPath = Path.GetFullPath(combined);
                return Result<string>.Success(fullPath);
            }
            catch (Exception ex)
            {
                return Result<string>.Fail(AppFailure.InternalError with { Message = ex.Message });
            }
        }
        else
        {
            string combined;
            if (path.StartsWith("/") || path.StartsWith("\\"))
            {
                var relative = path.TrimStart('/', '\\');
                combined = Path.Combine(_homePhysicalPath!, relative.Replace('/', Path.DirectorySeparatorChar));
            }
            else
            {
                var currentPhysical = _currentDirectory == "/" 
                    ? _homePhysicalPath! 
                    : Path.Combine(_homePhysicalPath!, _currentDirectory.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar));
                combined = Path.Combine(currentPhysical, path.Replace('/', Path.DirectorySeparatorChar));
            }

            try
            {
                string fullPath = Path.GetFullPath(combined);

                // Ensure it's inside _homePhysicalPath
                bool isInside = fullPath.Equals(_homePhysicalPath!, StringComparison.OrdinalIgnoreCase) ||
                                fullPath.StartsWith(_homePhysicalPath! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                                fullPath.StartsWith(_homePhysicalPath! + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

                if (!isInside)
                {
                    return Result<string>.Fail(AppFailure.Forbidden);
                }

                return Result<string>.Success(fullPath);
            }
            catch (Exception ex)
            {
                return Result<string>.Fail(AppFailure.InternalError with { Message = ex.Message });
            }
        }
    }
}
