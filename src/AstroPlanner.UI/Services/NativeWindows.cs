using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AstroPlanner.UI.Services;

/// <summary>Windows details that WPF does not expose.</summary>
internal static class NativeWindows
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Dark title bar matching the theme (Windows 10 20H1+ and Windows 11). Applied on
    /// SourceInitialized, before the window is painted for the first time; it does nothing on
    /// older versions of Windows.
    /// </summary>
    public static void UseDarkTitleBar(Window window) =>
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var enabled = 1;
            var applied = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) == 0
                          || DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int)) == 0;
            Debug.WriteLineIf(!applied, "Dark title bar is not supported on this version of Windows.");
        };

    /// <summary>Shows a window and brings it to the front (e.g. when the app is opened a second time).</summary>
    public static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            // Restores the previous state (maximized if it was), not always "normal".
            SystemCommands.RestoreWindow(window);
        }

        window.Show();
        window.Activate();
        // Usual trick: Windows does not allow stealing the focus, but it does allow raising the window briefly.
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }
}

/// <summary>
/// A single instance of AstroPlanner Pro per user session: two processes would write the plan and
/// the logbook at the same time. The second instance notifies the first one (which comes to the
/// front) and exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\AstroPlannerPro.SingleInstance.6BBEFD10";
    private const string EventName = @"Local\AstroPlannerPro.Activate.6BBEFD10";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, bool isFirstInstance, EventWaitHandle activate)
    {
        _mutex = mutex;
        IsFirstInstance = isFirstInstance;
        _activate = activate;
    }

    /// <summary>
    /// Null if the named objects cannot be opened (e.g. they were created by an instance running
    /// as administrator): the app then starts anyway, without the single-instance check, instead
    /// of exiting silently.
    /// </summary>
    public static SingleInstance? TryCreate()
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            try
            {
                return new SingleInstance(mutex, createdNew, new EventWaitHandle(false, EventResetMode.AutoReset, EventName));
            }
            catch
            {
                mutex.Dispose();
                throw;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            return null;
        }
    }

    public bool IsFirstInstance { get; }

    /// <summary>Asks the instance that is already open to show itself.</summary>
    public void ActivateFirstInstance() => _activate.Set();

    /// <summary>Calls <paramref name="onActivate"/> (on another thread) every time a second instance is opened.</summary>
    public void ListenForActivation(Action onActivate) =>
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _registration?.Unregister(null);
        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
        _activate.Dispose();
    }
}
