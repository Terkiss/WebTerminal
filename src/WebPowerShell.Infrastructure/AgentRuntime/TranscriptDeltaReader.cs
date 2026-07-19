using System.Text;
using System.Text.Json;

namespace WebPowerShell.Infrastructure.AgentRuntime;

public sealed class TranscriptDeltaReader
{
    public async Task<TranscriptDeltaReadResult> ReadDeltaAsync(
        ProviderSession session,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(session.LastTranscriptPath))
        {
            return TranscriptDeltaReadResult.Empty(session.TranscriptOffset);
        }

        var path = session.LastTranscriptPath;
        if (!File.Exists(path))
        {
            return TranscriptDeltaReadResult.Empty(session.TranscriptOffset);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (session.TranscriptOffset > stream.Length)
        {
            session.TranscriptOffset = 0;
        }

        stream.Seek(session.TranscriptOffset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        var entries = new List<TranscriptEntry>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            entries.Add(ParseLine(line));
        }

        var offset = stream.Position;
        session.TranscriptOffset = offset;
        return new TranscriptDeltaReadResult(entries, offset);
    }

    private static TranscriptEntry ParseLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var message = TryGetObject(root, "message");
            var role = GetString(root, "role") ??
                GetString(root, "speaker") ??
                (message.HasValue ? GetString(message.Value, "role") : null) ??
                (message.HasValue ? GetString(message.Value, "speaker") : null) ??
                GetString(root, "type");
            var content = GetContent(root) ??
                (message.HasValue ? GetContent(message.Value) : null) ??
                GetRawJson(root, "tool_calls") ??
                GetRawJson(root, "toolCalls") ??
                (message.HasValue ? GetRawJson(message.Value, "tool_calls") : null) ??
                (message.HasValue ? GetRawJson(message.Value, "toolCalls") : null);
            var eventType = GetString(root, "eventType") ?? GetString(root, "event_type") ?? GetString(root, "type");
            return new TranscriptEntry(line, role, content, eventType);
        }
        catch (JsonException)
        {
            return new TranscriptEntry(line, null, line, null);
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static JsonElement? TryGetObject(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Object
            ? property
            : null;
    }

    private static string? GetContent(JsonElement element)
    {
        if (element.TryGetProperty("content", out var content))
        {
            return ContentToString(content);
        }

        if (element.TryGetProperty("text", out var text))
        {
            return ContentToString(text);
        }

        return null;
    }

    private static string? ContentToString(JsonElement content)
    {
        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString(),
            JsonValueKind.Array => string.Join(
                Environment.NewLine,
                content.EnumerateArray()
                    .Select(ContentPartToString)
                    .Where(part => !string.IsNullOrWhiteSpace(part))),
            JsonValueKind.Object => GetString(content, "text") ?? GetString(content, "content") ?? content.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => content.GetRawText()
        };
    }

    private static string? ContentPartToString(JsonElement part)
    {
        return part.ValueKind switch
        {
            JsonValueKind.String => part.GetString(),
            JsonValueKind.Object => GetString(part, "text") ?? GetString(part, "content") ?? part.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => part.GetRawText()
        };
    }

    private static string? GetRawJson(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            ? property.GetRawText()
            : null;
    }
}

public sealed record TranscriptDeltaReadResult(IReadOnlyList<TranscriptEntry> Entries, long Offset)
{
    public static TranscriptDeltaReadResult Empty(long offset) => new([], offset);
}

public sealed record TranscriptEntry(
    string RawJsonLine,
    string? Role,
    string? Content,
    string? EventType);
