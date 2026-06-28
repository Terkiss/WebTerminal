using System;

namespace WebPowerShell.Domain.Entities
{
    public class PowerShellSession
    {
        public Guid SessionId { get; set; }
        public Guid TabId { get; set; }
        public Guid UserId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastActiveAt { get; set; }
    }
}
