namespace WebPowerShell.Infrastructure.AgentRuntime;

public sealed class TranscriptResponseExtractor
{
    public string? ExtractAssistantOutput(TranscriptDeltaReadResult delta)
    {
        for (var i = delta.Entries.Count - 1; i >= 0; i--)
        {
            var entry = delta.Entries[i];
            if (IsAssistantEntry(entry) && !string.IsNullOrWhiteSpace(entry.Content))
            {
                return entry.Content;
            }

            if (LooksLikeToolCall(entry.RawJsonLine))
            {
                return entry.RawJsonLine;
            }
        }

        return null;
    }

    private static bool IsAssistantEntry(TranscriptEntry entry)
    {
        return string.Equals(entry.Role, "assistant", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Role, "model", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Role, "agent", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeToolCall(string rawLine)
    {
        return rawLine.Contains("\"tool_calls\"", StringComparison.OrdinalIgnoreCase) ||
            rawLine.Contains("\"toolCalls\"", StringComparison.OrdinalIgnoreCase);
    }
}
