using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebPowerShell.Domain.Entities;
using WebPowerShell.Infrastructure.Persistence;

namespace WebPowerShell.Infrastructure.AgentRuntime;

public sealed class ProviderSessionStore : IProviderSessionStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProviderSessionStore> _logger;

    public ProviderSessionStore(IServiceScopeFactory scopeFactory, ILogger<ProviderSessionStore> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public IReadOnlyList<ProviderSession> LoadActive(DateTimeOffset now)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return dbContext.ProviderSessions
                .AsNoTracking()
                .Where(session => session.ExpiresAt > now && session.State != ProviderSessionState.Stopped.ToString())
                .OrderByDescending(session => session.CreatedAt)
                .AsEnumerable()
                .Select(ToSession)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load provider sessions from persistent store.");
            return [];
        }
    }

    public void Save(ProviderSession session)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var record = ToRecord(session);
            var existing = dbContext.ProviderSessions.Local.FirstOrDefault(candidate => candidate.SessionId == session.SessionId)
                ?? dbContext.ProviderSessions.Find(session.SessionId);

            if (existing == null)
            {
                dbContext.ProviderSessions.Add(record);
            }
            else
            {
                dbContext.Entry(existing).CurrentValues.SetValues(record);
            }

            dbContext.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist provider session {SessionId}.", session.SessionId);
        }
    }

    public void Delete(Guid sessionId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existing = dbContext.ProviderSessions.Find(sessionId);
            if (existing == null)
            {
                return;
            }

            dbContext.ProviderSessions.Remove(existing);
            dbContext.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete provider session {SessionId}.", sessionId);
        }
    }

    private static ProviderSession ToSession(ProviderSessionRecord record)
    {
        return new ProviderSession
        {
            SessionId = record.SessionId,
            OwnerUserId = record.OwnerUserId,
            Profile = record.Profile,
            DisplayName = record.DisplayName,
            State = Enum.TryParse<ProviderSessionState>(record.State, out var state) ? state : ProviderSessionState.Failed,
            ConversationId = record.ConversationId,
            AgyProcessId = null,
            LastAgyLogPath = record.LastAgyLogPath,
            LastTranscriptPath = record.LastTranscriptPath,
            TranscriptOffset = record.TranscriptOffset,
            ApiKeyHash = record.ApiKeyHash,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            ExpiresAt = record.ExpiresAt,
            FailureReason = record.FailureReason
        };
    }

    private static ProviderSessionRecord ToRecord(ProviderSession session)
    {
        return new ProviderSessionRecord
        {
            SessionId = session.SessionId,
            OwnerUserId = session.OwnerUserId,
            Profile = session.Profile,
            DisplayName = session.DisplayName,
            State = session.State.ToString(),
            ConversationId = session.ConversationId,
            LastAgyLogPath = session.LastAgyLogPath,
            LastTranscriptPath = session.LastTranscriptPath,
            TranscriptOffset = session.TranscriptOffset,
            ApiKeyHash = session.ApiKeyHash,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            ExpiresAt = session.ExpiresAt,
            FailureReason = session.FailureReason
        };
    }
}
