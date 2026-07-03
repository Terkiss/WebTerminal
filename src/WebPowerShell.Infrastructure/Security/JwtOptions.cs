namespace WebPowerShell.Infrastructure.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "WebTerminal";
    public string Audience { get; set; } = "WebTerminal";
    public int ExpiryDays { get; set; } = 1;
}
