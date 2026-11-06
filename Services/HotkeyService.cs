using System.Windows.Interop;
using TidyYard.Interop;

namespace TidyYard.Services;

/// <summary>
/// Global keyboard shortcuts, all Win+Alt+a letter. Messages arrive on a hidden message-only window, so no
/// visible window is needed. If another program already uses a combination, registering it fails quietly
/// and the app works as before without that shortcut.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int FirstId = 0x7D01;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];

    public HotkeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("TidyYardHotkeys")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0,
        });
        _source.AddHook(Hook);
    }

    /// <summary>Registers Win+Alt+<paramref name="virtualKey"/>. Returns false if the combination is taken.</summary>
    public bool Register(uint virtualKey, Action action)
    {
        var id = FirstId + _actions.Count;
        if (!NativeMethods.TryRegisterHotKey(_source.Handle, id, NativeMethods.MOD_WIN | NativeMethods.MOD_ALT, virtualKey))
        {
            return false;
        }

        _actions[id] = action;
        return true;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys)
        {
            NativeMethods.UnregisterHotKeyQuietly(_source.Handle, id);
        }

        _actions.Clear();
        _source.RemoveHook(Hook);
        _source.Dispose();
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }

        return IntPtr.Zero;
    }
}
