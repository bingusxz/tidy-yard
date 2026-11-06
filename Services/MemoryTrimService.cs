using System.Runtime;
using System.Windows.Threading;
using TidyYard.Interop;

namespace TidyYard.Services;

/// <summary>
/// Keeps an idle Tidy Yard small. Shortly after start-up, and then every few minutes, it collects garbage and
/// gives unused memory pages back to Windows. A tray widget spends nearly all its time idle, so this is what
/// Task Manager shows for most of the day. It costs a few milliseconds and the app needs no restart.
/// </summary>
public sealed class MemoryTrimService
{
    private static readonly TimeSpan FirstTrim = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly DispatcherTimer _timer = new() { Interval = FirstTrim };

    public MemoryTrimService() => _timer.Tick += (_, _) =>
    {
        _timer.Interval = Interval;
        TrimNow();
    };

    public void Start() => _timer.Start();

    public static void TrimNow()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        NativeMethods.TrimWorkingSet();
    }
}
