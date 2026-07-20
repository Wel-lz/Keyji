using System.Text;
using Keyji.Native;

namespace Keyji.Input;

/// <summary>
/// Помнит последнее «пользовательское» окно переднего плана. Нужен потому, что клик по
/// трей-иконке уводит foreground/focus на shell (<c>Shell_TrayWnd</c>) — доказано probe-логом:
/// в момент обработки клика редактор пользователя уже НЕ на переднем плане, и нацеливать
/// активацию/форс IME на <see cref="NativeMethods.GetForegroundWindow"/> бессмысленно
/// (уходит в taskbar). Хук <c>EVENT_SYSTEM_FOREGROUND</c> фиксирует смены фокуса и хранит
/// последнее окно, не принадлежащее Keyji и не являющееся shell'ом.
///
/// Создавать на UI-потоке: <c>WINEVENT_OUTOFCONTEXT</c> доставляет колбэк через очередь
/// сообщений потока-владельца хука (у WPF UI-поток качает pump). Делегат держим в поле —
/// иначе GC соберёт его, и колбэк упадёт.
/// </summary>
public sealed class ForegroundTracker : IDisposable
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private readonly NativeMethods.WinEventProc _callback; // держим ссылку живой
    private IntPtr _hook;

    /// <summary>Последнее окно переднего плана, не принадлежащее Keyji и не shell.</summary>
    public IntPtr LastUserWindow { get; private set; }

    /// <summary>
    /// Пользовательское окно стало передним. Ключевой сигнал return-time-переключения:
    /// клик по трею уводит фокус на shell и возвращает его на редактор — вот на этот
    /// возврат и вешается активация закреплённого языка (в момент клика редактор ещё
    /// в фоне, а WM_INPUTLANGCHANGEREQUEST в фон затирается при возврате фокуса —
    /// доказано probe: раскладка потока становилась нужной, но кириллица не печаталась).
    /// Вызывается на UI-потоке (владелец хука).
    /// </summary>
    public event Action<IntPtr>? UserWindowForeground;

    public ForegroundTracker()
    {
        _callback = OnForeground;
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _callback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);

        // Сид: окно, стоявшее на переднем плане до создания трекера (если это не мы/не shell).
        var current = NativeMethods.GetForegroundWindow();
        if (IsUserWindow(current))
            LastUserWindow = current;
    }

    private void OnForeground(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        // Исключение через native→managed границу недопустимо — гасим всё.
        try
        {
            if (IsUserWindow(hwnd))
            {
                LastUserWindow = hwnd;
                UserWindowForeground?.Invoke(hwnd);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private bool IsUserWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _ownProcessId)
            return false; // собственное окно Keyji

        var cls = new StringBuilder(128);
        NativeMethods.GetClassName(hwnd, cls, cls.Capacity);
        string name = cls.ToString();
        // Пустой класс = служебное окно shell (explorer): не цель ввода — отсеиваем.
        return name.Length > 0 && !ShellClasses.Contains(name);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
