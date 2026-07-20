using Keyji.Layouts;

namespace Keyji.Panel;

/// <summary>
/// Строка экрана-каталога «+ добавить язык»: пара имён + флаг установленности,
/// которого хватает вёрстке, чтобы выбрать правый аффорданс. Установлен → «+» (клик кладёт
/// язык в набор, <c>AddToSet</c>); пакета нет → «скачать» (deep-link в параметры Windows)
/// — контракт gated: неустановленный язык в набор не кладётся, поэтому
/// <see cref="LanguageProfile.ProfileId"/> такой записи не актуируется.
/// </summary>
public sealed class CatalogEntryView
{
    /// <summary>Язык этой строки (из курируемой библиотеки или установленного каталога).</summary>
    public required LanguageProfile Profile { get; init; }

    /// <summary>Нативный эндоним (без засечек): 日本語 / Deutsch.</summary>
    public required string Native { get; init; }

    /// <summary>Локализованное имя от ОС (умно сокращённое) + IME-дескриптор (Space Mono, small-caps): GERMAN.</summary>
    public required string Label { get; init; }

    /// <summary>Полное имя от ОС для тултипа, если <see cref="Label"/> был сокращён; иначе <c>null</c>.</summary>
    public string? Tooltip { get; init; }

    /// <summary>
    /// Пакет зарегистрирован на машине (<see cref="ILayoutService.IsRegisteredOnMachine"/>):
    /// true → путь «+ добавить», false → путь «скачать». Определяет и аффорданс, и действие клика.
    /// </summary>
    public required bool Installed { get; init; }
}
