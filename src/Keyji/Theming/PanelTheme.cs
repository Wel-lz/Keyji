using System.Windows;
using Wpf.Ui.Appearance;

namespace Keyji.Theming;

/// <summary>
/// Единственный владелец темы панели. Держит в ресурсах приложения ровно один
/// словарь — <c>Assets/Theme.Dark.xaml</c> или <c>Assets/Theme.Light.xaml</c> — и подменяет
/// его на лету, из-за чего вся вёрстка обязана биндиться <c>DynamicResource</c>.
///
/// Источник темы: <b>настройка-тристейт</b>
/// <see cref="ThemePreference"/>, персистится полем <c>Theme</c> в settings.json. При
/// <see cref="ThemePreference.System"/> тему гонит системный watcher; явные
/// <see cref="ThemePreference.Light"/>/<see cref="ThemePreference.Dark"/> его событие
/// игнорируют — это обобщение прежнего разового dev-<c>_debugOverride</c> в персистящееся
/// 3-значное состояние. Выбор ставит оверлей настроек; стартовое значение подаёт
/// <c>MainWindow</c> через <see cref="SetPreference"/> сразу после загрузки настроек.
/// </summary>
public static class PanelTheme
{
    private static ResourceDictionary? _applied;

    /// <summary>Тема, применённая к панели прямо сейчас.</summary>
    public static ApplicationTheme Current { get; private set; } = ApplicationTheme.Dark;

    /// <summary>
    /// Активный выбор темы. Решает, следует ли панель системной теме (<see cref="ThemePreference.System"/>)
    /// или зафиксирована (<see cref="ThemePreference.Light"/>/<see cref="ThemePreference.Dark"/>).
    /// Стартовое значение подаёт <c>MainWindow</c> из настроек через <see cref="SetPreference"/>.
    /// </summary>
    public static ThemePreference Preference { get; private set; } = ThemePreference.System;

    /// <summary>
    /// Подтягивает системную тему как baseline и подписывается на её смену. Вызывать до
    /// создания первого UI, чтобы панель не мигнула чужой темой на старте. Персист-выбор
    /// накатывается поверх позже — <c>MainWindow</c> зовёт <see cref="SetPreference"/>.
    /// </summary>
    public static void Initialize()
    {
        // Выравнивает и словарь WPF-UI (его контролы живут в окне первого запуска),
        // и наш — оба должны говорить об одной теме.
        ApplicationThemeManager.ApplySystemTheme();
        Apply(ApplicationThemeManager.GetAppTheme());

        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
    }

    /// <summary>
    /// Заводит живое отслеживание системной темы. Требует окна: WM_SETTINGCHANGE приходит
    /// на HWND, а не в процесс — годится и скрытый хост трея.
    /// Backdrop=None и updateAccents=false: watcher нужен нам только как источник события
    /// смены темы, менять фон/акценты чужого окна он не должен.
    /// </summary>
    public static void WatchSystemTheme(Window window)
        => SystemThemeWatcher.Watch(window, Wpf.Ui.Controls.WindowBackdropType.None, updateAccents: false);

    /// <summary>
    /// Сменить выбор темы: применить эффективную тему сразу (live-swap) и запомнить
    /// выбор для примирения с watcher. Персист (запись <c>Theme</c> в settings.json) — за
    /// вызывающим (оверлей настроек). Стартовый вызов из <c>MainWindow</c> накатывает
    /// сохранённое значение поверх системного baseline из <see cref="Initialize"/>.
    /// </summary>
    public static void SetPreference(ThemePreference preference)
    {
        Preference = preference;
        Apply(Effective(ApplicationThemeManager.GetAppTheme()));
    }

    /// <summary>Эффективная тема: явный выбор перекрывает системный, <see cref="ThemePreference.System"/> — следует ему.</summary>
    private static ApplicationTheme Effective(ApplicationTheme system) => Preference switch
    {
        ThemePreference.Light => ApplicationTheme.Light,
        ThemePreference.Dark => ApplicationTheme.Dark,
        _ => system,
    };

    private static void OnApplicationThemeChanged(ApplicationTheme theme, System.Windows.Media.Color accent)
    {
        // Примирение с watcher: системную смену слушаем, только пока выбор = System;
        // явные Light/Dark держат панель зафиксированной (обобщённый бывший _debugOverride).
        if (Preference != ThemePreference.System)
            return;
        Apply(theme);
    }

    private static void Apply(ApplicationTheme theme)
    {
        // ApplicationTheme у WPF-UI шире двух значений (есть HighContrast/Unknown) —
        // всё, что не Light, кладём в тёмную: у Keyji ровно две темы.
        bool light = theme == ApplicationTheme.Light;

        var dictionary = new ResourceDictionary
        {
            Source = new Uri(
                light
                    ? "pack://application:,,,/Assets/Theme.Light.xaml"
                    : "pack://application:,,,/Assets/Theme.Dark.xaml",
                UriKind.Absolute)
        };

        var merged = Application.Current.Resources.MergedDictionaries;

        // Сначала добавить новый (он идёт последним и перекрывает старый), потом убрать
        // старый — обратный порядок оставил бы кадр, в котором ключей темы нет вообще.
        merged.Add(dictionary);
        if (_applied is not null)
            merged.Remove(_applied);

        _applied = dictionary;
        Current = light ? ApplicationTheme.Light : ApplicationTheme.Dark;
    }
}
