using System;
using System.IO;
using Xunit;
using WebPowerShell.Domain.Common;
using WebPowerShell.Infrastructure.TeruTeruShell;

namespace WebPowerShell.Infrastructure.UnitTests
{
    public class VirtualFileSystemTests : IDisposable
    {
        private readonly string _baseDir;

        public VirtualFileSystemTests()
        {
            _baseDir = Environment.CurrentDirectory;
            // Clean up any existing home dir just in case
            var homeDir = Path.Combine(_baseDir, "home");
            if (Directory.Exists(homeDir))
            {
                // We shouldn't delete the actual home dir during real tests if it's shared, but here we can isolate.
                // Let's create unique user names to avoid conflicts.
            }
        }

        public void Dispose()
        {
        }

        [Fact]
        public void StandardUser_CannotAccessOutsideHomeDirectory_PathTraversal()
        {
            // Arrange
            var username = "testuser_traversal";
            var vfs = new VirtualFileSystem(isAdmin: false, username: username);

            // Act
            var result = vfs.ResolvePhysicalPath("../../");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(AppFailure.Forbidden.ErrorCode, result.Failure!.ErrorCode);
        }

        [Fact]
        public void StandardUser_CannotAccessOtherUsersDirectory()
        {
            // Arrange
            var username1 = "testuser_1";
            var username2 = "testuser_2";
            
            var vfs1 = new VirtualFileSystem(isAdmin: false, username: username1);
            var vfs2 = new VirtualFileSystem(isAdmin: false, username: username2); // Ensures home/testuser_2 exists

            // Act
            // Attempt to go from testuser_1's home to testuser_2's home
            var result = vfs1.ResolvePhysicalPath("../testuser_2");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(AppFailure.Forbidden.ErrorCode, result.Failure!.ErrorCode);
        }

        [Fact]
        public void AdminUser_CanAccessAnyDirectory()
        {
            // Arrange
            var username = "admin_user";
            var vfs = new VirtualFileSystem(isAdmin: true, username: username);

            // Act
            // Attempt to go up from current directory (which should work for admin)
            var result = vfs.ResolvePhysicalPath("../../");

            // Assert
            Assert.True(result.IsSuccess);
            // Result should contain the resolved path, not fail with Forbidden
        }
        
        [Fact]
        public void StandardUser_CanChangeDirectoryWithinHome()
        {
            // Arrange
            var username = "testuser_change";
            var vfs = new VirtualFileSystem(isAdmin: false, username: username);
            
            // Create a sub directory
            var homePhysicalPath = Path.Combine(_baseDir, "home", username);
            var subDir = Path.Combine(homePhysicalPath, "subdir");
            Directory.CreateDirectory(subDir);

            // Act
            var result = vfs.ChangeDirectory("subdir");

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("/subdir", vfs.CurrentDirectory);
            
            // Clean up
            Directory.Delete(subDir);
        }

        [Fact]
        public void StandardUser_ChangeDirectoryOutsideHome_Fails()
        {
            // Arrange
            var username = "testuser_changeout";
            var vfs = new VirtualFileSystem(isAdmin: false, username: username);

            // Act
            var result = vfs.ChangeDirectory("../../");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(AppFailure.Forbidden.ErrorCode, result.Failure!.ErrorCode);
            Assert.Equal("/", vfs.CurrentDirectory); // Directory shouldn't change
        }
    }
}
