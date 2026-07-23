namespace DailyGate.Windows.Service;

public sealed class ClientSession
{
    public bool Authenticated { get; private set; }
    public string? FullName { get; private set; }
    public DateTimeOffset? TestStartedAt { get; private set; }

    public void Authenticate(string fullName)
    {
        Authenticated = true;
        FullName = fullName;
        TestStartedAt ??= DateTimeOffset.UtcNow;
    }

    public void Clear()
    {
        Authenticated = false;
        FullName = null;
        TestStartedAt = null;
    }
}
