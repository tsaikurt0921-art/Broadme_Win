namespace Broadme.Win.Services.Auth;

public sealed class ControlAuthManager
{
    private readonly object _lock = new();
    private readonly HashSet<string> _authorizedPins = new();
    private readonly Dictionary<string, string> _pinTokenMap = new();
    private readonly Dictionary<string, DateTimeOffset> _pinAuthTime = new();

    private readonly TimeSpan _authorizationTimeout = TimeSpan.FromMinutes(30);

    public void Authorize(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return;

        lock (_lock)
        {
            CleanupExpired_NoLock();
            var key = pin.Trim();
            _authorizedPins.Add(key);
            _pinAuthTime[key] = DateTimeOffset.UtcNow;
        }
    }

    public void Revoke(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return;

        lock (_lock)
        {
            var key = pin.Trim();
            _authorizedPins.Remove(key);
            _pinAuthTime.Remove(key);

            if (_pinTokenMap.TryGetValue(key, out var token))
            {
                _pinTokenMap.Remove(key);
            }
        }
    }

    public void RevokeAll()
    {
        lock (_lock)
        {
            _authorizedPins.Clear();
            _pinTokenMap.Clear();
            _pinAuthTime.Clear();
        }
    }

    public bool IsAuthorized(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return false;

        lock (_lock)
        {
            CleanupExpired_NoLock();
            return _authorizedPins.Contains(pin.Trim());
        }
    }

    public bool HasAnyAuthorization()
    {
        lock (_lock)
        {
            CleanupExpired_NoLock();
            return _authorizedPins.Count > 0;
        }
    }

    public string? GetToken(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return null;

        lock (_lock)
        {
            CleanupExpired_NoLock();
            return _pinTokenMap.TryGetValue(pin.Trim(), out var token) ? token : null;
        }
    }

    public void BindToken(string pin, string token)
    {
        if (string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(token)) return;

        lock (_lock)
        {
            CleanupExpired_NoLock();
            var key = pin.Trim();
            if (!_authorizedPins.Contains(key)) return;

            _pinTokenMap[key] = token.Trim();
            _pinAuthTime[key] = DateTimeOffset.UtcNow;
        }
    }

    public string? RevokeByToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        lock (_lock)
        {
            var hit = _pinTokenMap.FirstOrDefault(kv => kv.Value == token.Trim());
            if (string.IsNullOrWhiteSpace(hit.Key)) return null;

            var pin = hit.Key;
            _pinTokenMap.Remove(pin);
            _pinAuthTime.Remove(pin);
            _authorizedPins.Remove(pin);
            return pin;
        }
    }

    private void CleanupExpired_NoLock()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _pinAuthTime
            .Where(kv => now - kv.Value > _authorizationTimeout)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var pin in expired)
        {
            _pinAuthTime.Remove(pin);
            _authorizedPins.Remove(pin);
            _pinTokenMap.Remove(pin);
        }
    }
}
