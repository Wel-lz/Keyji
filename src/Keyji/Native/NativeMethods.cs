using System.Runtime.InteropServices;
using System.Text;

namespace Keyji.Native;

/// <summary>
/// P/Invoke-обёртки над WinAPI для работы с раскладками. Всё — нативно,
/// без powershell.exe. Эмпирически проверено probe'ом на Win11:
/// InstallLayoutOrTip добавляет/удаляет язык (включая TIP-IME) unelevated,
/// обновляя HKL-список, Preload и User Profile\Languages одним вызовом.
/// </summary>
internal static class NativeMethods
{
    /// <summary>Возвращает список загруженных раскладок (HKL) текущего процесса/пользователя.</summary>
    [DllImport("user32.dll")]
    internal static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);

    /// <summary>HKL раскладки указанного потока (0 = текущий).</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetKeyboardLayout(uint idThread);

    /// <summary>
    /// input.dll — полу-документированный экспорт. psz: "LangID:KLID" (обычная раскладка)
    /// либо "LangID:{CLSID}{GUID}" (TIP/IME). Несколько — через ';'.
    /// </summary>
    [DllImport("input.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InstallLayoutOrTip(string psz, uint dwFlags);

    /// <summary>Флаг InstallLayoutOrTip: удалить язык/раскладку из пользовательского списка.</summary>
    internal const uint ILOT_UNINSTALL = 0x00000001;

    // --- Переключение активной раскладки в переднем окне ---

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    internal static extern IntPtr PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    internal const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    internal const int INPUTLANGCHANGE_SYSTEMCHARSET = 0x0001;

    /// <summary>Поток-владелец окна (для чтения активной раскладки после асинхронной активации).</summary>
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    /// <summary>Overload: заодно отдаёт PID окна (фильтр «своих» окон в трекере фокуса).</summary>
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // --- Трекер последнего пользовательского окна (клик по трею уводит фокус на shell) ---

    internal delegate void WinEventProc(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    // --- Форс режима ввода IME (форс-хирагана) ---

    /// <summary>
    /// Окно скрытого IME указанного окна (imm32). Целиться нужно в СФОКУСИРОВАННОЕ окно
    /// (<see cref="GUITHREADINFO.hwndFocus"/>), не в top-level frame — иначе SET уходит
    /// в чужой input-контекст (ложный «зелёный»).
    /// </summary>
    [DllImport("imm32.dll")]
    internal static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    /// <summary>Синхронный SendMessage: для WM_IME_CONTROL GET-варианты возвращают значение через результат.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    internal static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    internal const uint WM_IME_CONTROL = 0x0283;
    internal const int IMC_GETCONVERSIONMODE = 0x0001;
    internal const int IMC_SETCONVERSIONMODE = 0x0002;
    internal const int IMC_GETOPENSTATUS = 0x0005;
    internal const int IMC_SETOPENSTATUS = 0x0006;

    /// <summary>
    /// Golden-режим хираганы: NATIVE(0x1) | FULLSHAPE(0x8) | ROMAN(0x10). Значение
    /// откалибровано эмпирически (набранное «a» → «あ»); бит ROMAN зависит от
    /// пользовательской настройки «Кана» (выкл → romaji-ввод). Настраиваемый режим — post-v0.1.
    /// </summary>
    internal const int IME_CMODE_HIRAGANA = 0x19;

    /// <summary>Инфо о GUI-потоке; нужен <see cref="GUITHREADINFO.hwndFocus"/> переднего потока (idThread=0).</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [StructLayout(LayoutKind.Sequential)]
    internal struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public int rcCaretLeft, rcCaretTop, rcCaretRight, rcCaretBottom;
    }

    // --- Диагностика цели форса (probe: какое окно на переднем плане в момент триггера) ---

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    // --- Каталог установленного: локализованные имена ---

    /// <summary>
    /// Локализованное имя языка по BCP-47 тегу (напр. "en-US" → "Английский (США)").
    /// Надёжнее кэша User Profile\CachedLanguageName (тот populated лениво).
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetLocaleInfoEx(string lpLocaleName, uint LCType, StringBuilder lpLCData, int cchData);

    /// <summary>Локализованное отображаемое имя локали ("English (United States)").</summary>
    internal const uint LOCALE_SLOCALIZEDDISPLAYNAME = 0x00000002;

    /// <summary>Имя языка на нём самом — нативный эндоним ("日本語", "Русский"), часть пары имён.</summary>
    internal const uint LOCALE_SNATIVELANGUAGENAME = 0x00000004;

    /// <summary>
    /// Разрешить indirect-строку вида "@dll,-N" в локализованный текст под UI-язык.
    /// Description/Layout Display Name в реестре часто indirect.
    /// </summary>
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);
}
