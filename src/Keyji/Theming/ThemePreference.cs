namespace Keyji.Theming;

/// <summary>
/// Выбор темы панели как настройка. Персистится строкой
/// в поле <c>Theme</c> файла settings.json; дефолт (поля нет) = <see cref="System"/>.
/// Примирение с live-watcher — в <see cref="PanelTheme"/>:
/// <see cref="System"/> держит <c>SystemThemeWatcher</c> живым (тема следует ОС), явные
/// <see cref="Light"/>/<see cref="Dark"/> его игнорируют (обобщение разового dev-оверрайда).
/// </summary>
public enum ThemePreference
{
    /// <summary>Следовать системной теме Windows (watcher жив).</summary>
    System,

    /// <summary>Всегда светлая, независимо от системной (watcher игнорируется).</summary>
    Light,

    /// <summary>Всегда тёмная, независимо от системной (watcher игнорируется).</summary>
    Dark,
}
