using System;

namespace WebPowerShell.Infrastructure.TeruTeruShell;

public class ShellParser
{
    public (string CommandName, string[] Args) Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (string.Empty, Array.Empty<string>());

        var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var commandName = parts[0];
        var args = new string[parts.Length - 1];
        Array.Copy(parts, 1, args, 0, args.Length);

        return (commandName, args);
    }
}
