using System.Security.Cryptography;

namespace Broadme.Win.Services.Auth;

public sealed class ControlPinManager
{
    private readonly object _lock = new();

    public string? CurrentPin { get; private set; }
    public DateTimeOffset? Expiry { get; private set; }
    public bool IsActive => CurrentPin is not null && Expiry is not null && DateTimeOffset.UtcNow < Expiry;

    public string Generate(int ttlSeconds = 600)
    {
        lock (_lock)
        {
            CurrentPin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            Expiry = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds);
            return CurrentPin;
        }
    }

    public bool Validate(string pin)
    {
        lock (_lock)
        {
            return IsActive && string.Equals(CurrentPin, pin?.Trim(), StringComparison.Ordinal);
        }
    }

    public void Revoke()
    {
        lock (_lock)
        {
            CurrentPin = null;
            Expiry = null;
        }
    }
}
