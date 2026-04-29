namespace Broadme.Win.Services.Auth;

public sealed class SessionManager
{
    private sealed record Session(string Token, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

    private readonly object _lock = new();
    private readonly Dictionary<string, Session> _sessions = new();

    public string CreateSession(int initialTtlSeconds = 600)
    {
        var token = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        lock (_lock)
        {
            _sessions[token] = new Session(token, now, now.AddSeconds(initialTtlSeconds));
        }
        return token;
    }

    public bool ValidateAndExtend(string token, int extendSeconds = 600)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(token, out var session)) return false;
            if (DateTimeOffset.UtcNow >= session.ExpiresAt)
            {
                _sessions.Remove(token);
                return false;
            }
            _sessions[token] = session with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(extendSeconds) };
            return true;
        }
    }

    public void EndSession(string token)
    {
        lock (_lock) _sessions.Remove(token);
    }
}
