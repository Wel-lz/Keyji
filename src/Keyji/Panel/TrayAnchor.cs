using System.Runtime.InteropServices;

namespace Keyji.Panel;

/// <summary>
/// Прижимает панель к тому углу, где реально живёт трей-иконка.
///
/// Наивный якорь по <c>SystemParameters.WorkArea</c> верен только в одном
/// случае: панель задач внизу основного монитора. У пользователя с тремя мониторами и
/// панелью задач ВВЕРХУ не-основного экрана панель уезжала в правый-нижний угол чужого
/// монитора. Поэтому монитор и сторону берём от самого окна панели задач
/// (<c>Shell_TrayWnd</c>), а координаты считаем в физических пикселях: у мониторов
/// может быть разный DPI (PerMonitorV2), и WPF-овские DIP тут не универсальны.
/// </summary>
internal static class TrayAnchor
{
    private const uint MonitorDefaultToPrimary = 1;
    private const uint MonitorDefaultToNearest = 2;
    private const uint MdtEffectiveDpi = 0;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>
    /// Переставляет окно в угол рабочей области монитора с треем.
    /// </summary>
    /// <param name="hwnd">Хэндл окна панели (после <c>Show</c> — до него размера ещё нет).</param>
    /// <param name="widthDip">Ширина окна в DIP (WPF <c>ActualWidth</c>).</param>
    /// <param name="heightDip">Высота окна в DIP (WPF <c>ActualHeight</c>).</param>
    /// <param name="shadowMargin">Поле под тень внутри окна, DIP: карта начинается не от края окна.</param>
    /// <param name="gap">Зазор между картой и краем рабочей области, DIP.</param>
    /// <remarks>
    /// Размер берём из WPF <c>ActualWidth/ActualHeight</c>, а НЕ из <c>GetWindowRect</c>:
    /// панель — <c>SizeToContent</c>, и при смене тела (каталог/список) нативный размер HWND
    /// отстаёт от финального на один layout-проход к моменту <c>SizeChanged</c>. Заниженная
    /// высота роняла нижний край карты под панель задач (низ якорится к <c>rcWork.Bottom</c>).
    /// </remarks>
    public static void Snap(IntPtr hwnd, double widthDip, double heightDip, double shadowMargin, double gap)
    {
        if (hwnd == IntPtr.Zero || widthDip <= 0 || heightDip <= 0)
            return;

        var monitor = TrayMonitor();
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;

        double scale = DpiScale(monitor);
        int inset = (int)Math.Round((shadowMargin - gap) * scale);

        int width = (int)Math.Round(widthDip * scale);
        int height = (int)Math.Round(heightDip * scale);

        // Сторона панели задач = там, где рабочая область не достаёт до края монитора.
        bool taskbarTop = info.rcWork.Top > info.rcMonitor.Top;
        bool taskbarLeft = info.rcWork.Left > info.rcMonitor.Left;

        int x = taskbarLeft
            ? info.rcWork.Left - inset
            : info.rcWork.Right - width + inset;

        int y = taskbarTop
            ? info.rcWork.Top - inset
            : info.rcWork.Bottom - height + inset;

        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    /// <summary>Монитор, на котором висит панель задач; при отсутствии окна трея — основной.</summary>
    private static IntPtr TrayMonitor()
    {
        var tray = FindWindow("Shell_TrayWnd", null);
        return tray != IntPtr.Zero
            ? MonitorFromWindow(tray, MonitorDefaultToNearest)
            : MonitorFromWindow(IntPtr.Zero, MonitorDefaultToPrimary);
    }

    private static double DpiScale(IntPtr monitor)
        => GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint dpiX, out _) == 0 && dpiX > 0
            ? dpiX / 96.0
            : 1.0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, uint dpiType, out uint dpiX, out uint dpiY);
}
