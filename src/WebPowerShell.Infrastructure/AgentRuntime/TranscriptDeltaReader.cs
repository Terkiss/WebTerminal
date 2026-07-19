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
            var role = GetString(root, "role") ?? GetString(root, "speaker") ?? GetString(root, "type");
            var content = GetString(root, "content") ?? GetString(root, "text") ?? GetNestedString(root, "message", "content");
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

    private static string? GetNestedString(JsonElement element, string parentName, string propertyName)
    {
        if (!element.TryGetProperty(parentName, out var parent) || parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(parent, propertyName);
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
