using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Keyji.Diagnostics;
using Keyji.Native;
using Microsoft.Win32;

namespace Keyji.Layouts;

/// <summary>
/// Реализация <see cref="ILayoutService"/> поверх WinAPI.
/// Add/remove проверены эмпирически (probe, Win11): один InstallLayoutOrTip
/// обновляет HKL-список, Preload и User Profile\Languages, unelevated.
/// </summary>
public sealed class LayoutService : ILayoutService
{
    public IReadOnlyList<LayoutInfo> GetActiveLayouts()
    {
        int count = NativeMethods.GetKeyboardLayoutList(0, null);
        if (count <= 0)
            return Array.Empty<LayoutInfo>();

        var handles = new IntPtr[count];
        NativeMethods.GetKeyboardLayoutList(count, handles);

        var result = new List<LayoutInfo>(count);
        foreach (var hkl in handles)
            result.Add(LayoutInfo.FromHkl(hkl));
        return result;
    }

    public bool IsEnabled(LanguageProfile profile) =>
        GetActiveLayouts().Any(l => l.LangId == profile.LangId);

    public bool EnableLanguage(LanguageProfile profile) =>
        NativeMethods.InstallLayoutOrTip(profile.ToInstallString(), 0);

    public bool DisableLanguage(LanguageProfile profile) =>
        NativeMethods.InstallLayoutOrTip(profile.ToInstallString(), NativeMethods.ILOT_UNINSTALL);

    public bool IsRegisteredOnMachine(LanguageProfile profile) =>
        profile.IsTip ? IsTipRegistered(profile) : IsKlidRegistered(profile);

    /// <summary>
    /// TIP присутствует в машинном каталоге CTF: ветка профиля для его LangId.
    /// Пути — те же, что читаются ради DisplayName; здесь важен лишь
    /// факт существования ключа. profileid TIP = "{CLSID}{GUID}" (две GUID-строки).
    /// </summary>
    private static bool IsTipRegistered(LanguageProfile profile)
    {
        const int guidLength = 38; // "{8-4-4-4-12}"
        if (profile.ProfileId.Length < guidLength * 2)
            return false;

        string clsid = profile.ProfileId[..guidLength];
        string guid = profile.ProfileId[guidLength..];
        string path = $@"SOFTWARE\Microsoft\CTF\TIP\{clsid}\LanguageProfile\0x0000{profile.LangId:x4}\{guid}";

        using var key = Registry.LocalMachine.OpenSubKey(path);
        return key is not null;
    }

    /// <summary>Обычная раскладка присутствует: ключ по её KLID (profileid = 8 hex).</summary>
    private static bool IsKlidRegistered(LanguageProfile profile)
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\{profile.ProfileId}");
        return key is not null;
    }

    /// <summary>Японский Microsoft IME (LangID 0x0411) — единственный, для кого форсим режим (флагман v0.1).</summary>
    private const ushort JapaneseLangId = 0x0411;

    public void SwitchTo(LanguageProfile profile, IntPtr targetWindow)
    {
        var target = GetActiveLayouts().FirstOrDefault(l => l.LangId == profile.LangId);
        if (target is null || targetWindow == IntPtr.Zero)
            return;

        uint targetThread = NativeMethods.GetWindowThreadProcessId(targetWindow, IntPtr.Zero);
        Trace.Line($"SwitchTo {profile.DisplayName}: target={Describe(targetWindow)} " +
                   $"foreground={Describe(NativeMethods.GetForegroundWindow())} focus={Describe(FocusedWindow(targetThread))}");

        NativeMethods.PostMessage(
            targetWindow,
            NativeMethods.WM_INPUTLANGCHANGEREQUEST,
            new IntPtr(NativeMethods.INPUTLANGCHANGE_SYSTEMCHARSET),
            target.Hkl);

        // Диагностика (для любого языка): реально ли раскладка целевого потока стала нужной?
        // Активация WM_INPUTLANGCHANGE асинхронна — опрашиваем до ~500 мс.
        bool active = WaitForActiveLangId(targetThread, profile.LangId);
        Trace.Line(active
            ? $"  {profile.DisplayName} активировался на целевом потоке (0x{profile.LangId:x4})"
            : $"  таймаут ожидания активации {profile.DisplayName} (0x{profile.LangId:x4})");

        // Форс режима — только для японского (флагманский путь).
        if (profile.LangId == JapaneseLangId)
            ForceHiragana(targetWindow, targetThread);
    }

    public bool IsJapaneseActive(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return false;

        uint thread = NativeMethods.GetWindowThreadProcessId(window, IntPtr.Zero);
        uint hkl = (uint)NativeMethods.GetKeyboardLayout(thread).ToInt64();
        return (hkl & 0xFFFF) == JapaneseLangId;
    }

    public void ForceJapaneseMode(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero)
            return;

        uint thread = NativeMethods.GetWindowThreadProcessId(targetWindow, IntPtr.Zero);
        ForceHiragana(targetWindow, thread);
    }

    /// <summary>
    /// Выставить open=1 + хирагану на СФОКУСИРОВАННОМ окне целевого потока (не frame).
    /// Активацию JP к этому моменту уже дождались в <see cref="SwitchTo"/>.
    /// </summary>
    private static void ForceHiragana(IntPtr targetWindow, uint targetThread)
    {
        var focus = FocusedWindow(targetThread);
        if (focus == IntPtr.Zero)
            focus = targetWindow; // фолбэк: сам top-level, если фокус потока не читается

        var ime = NativeMethods.ImmGetDefaultIMEWnd(focus);
        if (ime == IntPtr.Zero)
        {
            Trace.Line($"  IME-окно не найдено для focus={Describe(focus)} — форс пропущен");
            return;
        }

        // Ось open/close отдельна от режима конверсии: пока open=0, режим не применяется.
        NativeMethods.SendMessage(ime, NativeMethods.WM_IME_CONTROL,
            new IntPtr(NativeMethods.IMC_SETOPENSTATUS), new IntPtr(1));
        NativeMethods.SendMessage(ime, NativeMethods.WM_IME_CONTROL,
            new IntPtr(NativeMethods.IMC_SETCONVERSIONMODE), new IntPtr(NativeMethods.IME_CMODE_HIRAGANA));

        long conv = NativeMethods.SendMessage(ime, NativeMethods.WM_IME_CONTROL,
            new IntPtr(NativeMethods.IMC_GETCONVERSIONMODE), IntPtr.Zero).ToInt64();
        long open = NativeMethods.SendMessage(ime, NativeMethods.WM_IME_CONTROL,
            new IntPtr(NativeMethods.IMC_GETOPENSTATUS), IntPtr.Zero).ToInt64();
        Trace.Line($"  после форса на focus={Describe(focus)}: open={open} conv=0x{conv:X} " +
                   "(GET-readback — НЕ доказательство, проверять глифом)");
    }

    /// <summary>
    /// Активная раскладка потока == langId? Опрашиваем коротко (~225 мс): вызывается из
    /// хук-колбэка на UI-потоке, окно уже foreground — подтверждение быстрое, долгий Sleep
    /// заморозил бы очередь сообщений.
    /// </summary>
    private static bool WaitForActiveLangId(uint thread, ushort langId)
    {
        for (int i = 0; i < 15; i++)
        {
            uint hkl = (uint)NativeMethods.GetKeyboardLayout(thread).ToInt64();
            if ((hkl & 0xFFFF) == langId)
                return true;
            Thread.Sleep(15);
        }
        return false;
    }

    /// <summary>Сфокусированное окно указанного потока — цель форса IME.</summary>
    private static IntPtr FocusedWindow(uint thread)
    {
        var gti = new NativeMethods.GUITHREADINFO { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        return NativeMethods.GetGUIThreadInfo(thread, ref gti) ? gti.hwndFocus : IntPtr.Zero;
    }

    /// <summary>«class 'title'» окна для probe-лога цели форса.</summary>
    private static string Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return "<null>";
        var cls = new StringBuilder(128);
        NativeMethods.GetClassName(hwnd, cls, cls.Capacity);
        var title = new StringBuilder(128);
        NativeMethods.GetWindowText(hwnd, title, title.Capacity);
        return $"{cls} '{title}'";
    }
}
