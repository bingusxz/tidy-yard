using System.Windows.Interop;
using TidyYard.Interop;

namespace TidyYard.Services;

/// <summary>
/// One global keyboard shortcut. Messages arrive on a hidden message-only window, so no
/// visible window is needed. If another program already uses the combination, registering
/// fails quietly and the app works as before without the shortcut.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x7D01;

    private readonly HwndSource _source;
    private readonly Action _action;
    private bool _registered;

    public HotkeyService(Action action)
    {
        _action = action;
        _source = new HwndSource(new HwndSourceParameters("TidyYardHotkeys")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0,
        });
        _source.AddHook(Hook);
    }

    /// <summary>Registers Win+Alt+<paramref name="virtualKey"/>. Returns false if the combination is taken.</summary>
    public bool Register(uint virtualKey)
    {
        _registered = NativeMethods.TryRegisterHotKey(
            _source.Handle, HotkeyId, NativeMethods.MOD_WIN | NativeMethods.MOD_ALT, virtualKey);
        return _registered;
    }

    public void Dispose()
    {
        if (_registered)
        {
            NativeMethods.UnregisterHotKeyQuietly(_source.Handle, HotkeyId);
            _registered = false;
        }

        _source.RemoveHook(Hook);
        _source.Dispose();
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _action();
        }

        return IntPtr.Zero;
    }
}
