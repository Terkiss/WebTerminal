using System;
using WebPowerShell.Application.Sessions.Common;
using WebPowerShell.Domain.Entities;
using Xunit;

namespace WebPowerShell.Application.UnitTests.Sessions
{
    public class PowerShellSessionTests
    {
        [Fact]
        public void PowerShellSession_ShouldSetPropertiesCorrectly()
        {
            // Arrange
            var sessionId = Guid.NewGuid();
            var tabId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var createdAt = DateTimeOffset.UtcNow;
            var lastActiveAt = DateTimeOffset.UtcNow.AddMinutes(5);

            // Act
            var session = new PowerShellSession
            {
                SessionId = sessionId,
                TabId = tabId,
                UserId = userId,
                CreatedAt = createdAt,
                LastActiveAt = lastActiveAt
            };

            // Assert
            Assert.Equal(sessionId, session.SessionId);
            Assert.Equal(tabId, session.TabId);
            Assert.Equal(userId, session.UserId);
            Assert.Equal(createdAt, session.CreatedAt);
            Assert.Equal(lastActiveAt, session.LastActiveAt);
        }

        [Fact]
        public void PowerShellStreamData_ShouldSetPropertiesCorrectly()
        {
            // Arrange
            var type = "Output";
            var content = "Hello, PowerShell!";
            var timestamp = DateTimeOffset.UtcNow;

            // Act
            var streamData = new PowerShellStreamData
            {
                Type = type,
                Content = content,
                Timestamp = timestamp
            };

            // Assert
            Assert.Equal(type, streamData.Type);
            Assert.Equal(content, streamData.Content);
            Assert.Equal(timestamp, streamData.Timestamp);
        }
    }
}
