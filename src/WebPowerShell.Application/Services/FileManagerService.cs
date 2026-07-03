using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WebPowerShell.Application.Services
{
    public class FileItemDto
    {
        public string Name { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public string Path { get; set; } = string.Empty;
    }

    public class FileManagerService
    {
        private readonly string _basePath;

        public FileManagerService()
        {
            // Set base directory to the user's home directory
            _basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private string GetSafePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) || relativePath == "/")
            {
                return _basePath;
            }

            relativePath = relativePath.TrimStart('/', '\\');

            string basePathWithSeparator = _basePath.EndsWith(Path.DirectorySeparatorChar.ToString()) 
                ? _basePath 
                : _basePath + Path.DirectorySeparatorChar;

            if (Path.IsPathRooted(relativePath))
            {
                throw new UnauthorizedAccessException("Absolute paths are not allowed.");
            }

            var combined = Path.Combine(_basePath, relativePath);
            var fullPath = Path.GetFullPath(combined);

            try
            {
                if (File.Exists(fullPath))
                {
                    var target = new FileInfo(fullPath).ResolveLinkTarget(true);
                    if (target != null) fullPath = target.FullName;
                }
                else if (Directory.Exists(fullPath))
                {
                    var target = new DirectoryInfo(fullPath).ResolveLinkTarget(true);
                    if (target != null) fullPath = target.FullName;
                }
            }
            catch
            {
                // Ignore resolution errors
            }

            if (!fullPath.StartsWith(basePathWithSeparator, StringComparison.OrdinalIgnoreCase) && 
                !fullPath.Equals(_basePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Path Traversal detected.");
            }

            return fullPath;
        }

        public string GetRelativePath(string fullPath)
        {
            var relative = fullPath.Substring(_basePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.IsNullOrEmpty(relative) ? "/" : "/" + relative.Replace('\\', '/');
        }

        public IEnumerable<FileItemDto> ListFiles(string relativePath)
        {
            var targetPath = GetSafePath(relativePath);
            
            if (!Directory.Exists(targetPath))
            {
                throw new DirectoryNotFoundException("Directory not found.");
            }

            var dirInfo = new DirectoryInfo(targetPath);
            var items = new List<FileItemDto>();

            try
            {
                // Add directories
                foreach (var dir in dirInfo.GetDirectories())
                {
                    items.Add(new FileItemDto
                    {
                        Name = dir.Name,
                        IsDirectory = true,
                        Size = 0,
                        LastModified = dir.LastWriteTime,
                        Path = GetRelativePath(dir.FullName)
                    });
                }

                // Add files
                foreach (var file in dirInfo.GetFiles())
                {
                    items.Add(new FileItemDto
                    {
                        Name = file.Name,
                        IsDirectory = false,
                        Size = file.Length,
                        LastModified = file.LastWriteTime,
                        Path = GetRelativePath(file.FullName)
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore items that cannot be accessed
            }

            return items.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name);
        }

        public void DeleteItem(string relativePath)
        {
            var targetPath = GetSafePath(relativePath);

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            else if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, true);
            }
            else
            {
                throw new FileNotFoundException("Item not found.");
            }
        }

        public string GetFilePath(string relativePath)
        {
            var targetPath = GetSafePath(relativePath);
            if (!File.Exists(targetPath))
            {
                throw new FileNotFoundException("File not found.");
            }
            return targetPath;
        }

        public void SaveFile(string relativeDirectory, string fileName, Stream contentStream)
        {
            var targetDir = GetSafePath(relativeDirectory);
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var sanitizedFileName = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(sanitizedFileName))
            {
                throw new ArgumentException("Invalid file name.");
            }

            var targetFile = Path.Combine(targetDir, sanitizedFileName);
            var safeTargetFile = Path.GetFullPath(targetFile);

            try
            {
                if (File.Exists(safeTargetFile))
                {
                    var target = new FileInfo(safeTargetFile).ResolveLinkTarget(true);
                    if (target != null) safeTargetFile = target.FullName;
                }
            }
            catch {}

            string basePathWithSeparator = _basePath.EndsWith(Path.DirectorySeparatorChar.ToString()) 
                ? _basePath 
                : _basePath + Path.DirectorySeparatorChar;

            if (!safeTargetFile.StartsWith(basePathWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Path Traversal detected.");
            }

            using (var fs = new FileStream(safeTargetFile, FileMode.Create, FileAccess.Write))
            {
                contentStream.CopyTo(fs);
            }
        }
    }
}
