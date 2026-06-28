using System;

namespace WebPowerShell.Domain.Entities
{
    public class AuditLog
    {
        public long Id { get; set; }
        public Guid UserId { get; set; }
        public string UsernameSnapshot { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string TabId { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public DateTimeOffset ExecutedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public long? DurationMs { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string ResultStatus { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
    }
}
