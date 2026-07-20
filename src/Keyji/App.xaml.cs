using System.Windows;
using Keyji.Localization;
using Keyji.Settings;
using Keyji.Theming;

namespace Keyji;

/// <summary>
/// Точка входа приложения. ShutdownMode = OnExplicitShutdown: закрытие окна
/// прячет его в трей, реальный выход — только через пункт меню трея.
/// Модель tray-menu-first: и обычный запуск, и автозапуск уходят в трей —
/// основной UI это ПКМ-меню трей-иконки, окно настроек поднимается по «Настройки».
/// </summary>
public partial class App : Application
{
    /// <summary>Аргумент автозапуска: стартовать свёрнутым в трей (совместимость).</summary>
    public const string TrayArg = "--tray";

    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Язык UI — до создания любого UI, иначе первый кадр покажет чужой язык.
        // Статическая модель: культура = CurrentUICulture ОС, fallback на английский.
        Loc.Initialize();

        // Тема панели — до создания любого UI, иначе первый кадр мигнёт чужой темой.
        PanelTheme.Initialize();

        // Признак первого запуска читаем ДО конструктора MainWindow: он сразу пишет
        // settings.json (нормализация закреплённого), что затёрло бы этот сигнал.
        bool firstRun = !SettingsService.Exists();

        _mainWindow = new MainWindow(firstRun);

        if (firstRun)
            // Онбординг: один раз показываем урезанное окно с приветствием —
            // объясняем, что основной UI в ПКМ-меню трея, и предлагаем автозапуск.
            // Показ окна попутно регистрирует трей-иконку (без гонки с StartInTray).
            _mainWindow.ShowFirstRunWelcome();
        else
            // tray-menu-first: обычный запуск сворачивается в трей, окна не показываем.
            _mainWindow.StartInTray();

        // Живое отслеживание системной темы: WM_SETTINGCHANGE приходит на HWND,
        // поэтому подписываемся ПОСЛЕ показа окна — оба пути выше окно уже создали
        // (StartInTray делает Show/Hide именно ради регистрации трей-иконки).
        PanelTheme.WatchSystemTheme(_mainWindow);
    }
}
