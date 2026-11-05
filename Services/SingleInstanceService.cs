using TidyYard.Interop;

namespace TidyYard.Services;

/// <summary>
/// One Tidy Yard per user session. A second launch signals the first and exits.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\TidyYard.SingleInstance";
    private const string ActivateEventName = @"Local\TidyYard.Activate";

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _registration;

    public bool TryClaim()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        return true;
    }

    /// <summary>Runs <paramref name="onActivate"/> on a pool thread whenever another launch signals.</summary>
    public void ListenForActivation(Action onActivate)
    {
        if (_activateEvent is null)
        {
            throw new InvalidOperationException("Call TryClaim first.");
        }

        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public static void SignalFirstInstance()
    {
        NativeMethods.AllowAnyProcessToSetForeground();
        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        activate.Set();
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activateEvent?.Dispose();
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
