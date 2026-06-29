using System;

namespace WebPowerShell.Application.Common.Models;

public class ShellOutputPayload
{
    public string Type { get; set; } = string.Empty; // stdout, stderr, system, error
    public string Text { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
