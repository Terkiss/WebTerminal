using System;

namespace WebPowerShell.Application.Sessions.Common
{
    public class PowerShellStreamData
    {
        public string Type { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTimeOffset Timestamp { get; set; }
    }
}
