namespace Keyji.Layouts;

/// <summary>
/// Управление раскладками клавиатуры Windows нативно (без powershell.exe).
/// Ядро ценности Keyji — включить/выключить редко-нужный язык одним действием
/// (Enable/Disable). Перечисление и переключение — вспомогательные.
/// </summary>
public interface ILayoutService
{
    /// <summary>Раскладки, стоящие в пользовательском списке сейчас.</summary>
    IReadOnlyList<LayoutInfo> GetActiveLayouts();

    /// <summary>Стоит ли язык в пользовательском списке (по LangId).</summary>
    bool IsEnabled(LanguageProfile profile);

    /// <summary>
    /// Добавить язык в пользовательский список (toggle on). Работает для уже
    /// установленных в системе языков/IME. Если пакет не установлен — язык
    /// добавится как «partial» (докачка FOD — вне ядра v0.1).
    /// </summary>
    /// <returns>true при успехе InstallLayoutOrTip.</returns>
    bool EnableLanguage(LanguageProfile profile);

    /// <summary>Убрать язык из пользовательского списка (toggle off).</summary>
    /// <returns>true при успехе.</returns>
    bool DisableLanguage(LanguageProfile profile);

    /// <summary>
    /// Зарегистрирован ли TIP/KLID языка на машине (бинари IME/раскладки присутствуют).
    /// Граница enable ↔ deep-link: если TIP/KLID есть в
    /// машинном каталоге — язык включается напрямую (<see cref="EnableLanguage"/>);
    /// если нет — <see cref="InstallLayoutOrTip"/> не докачает пакет, нужен deep-link
    /// в Settings. Проверка: TIP → <c>HKLM\…\CTF\TIP\{CLSID}\LanguageProfile\
    /// 0x0000&lt;langid&gt;\{GUID}</c>, KLID → <c>HKLM\…\Keyboard Layouts\&lt;KLID&gt;</c>.
    /// </summary>
    bool IsRegisteredOnMachine(LanguageProfile profile);

    /// <summary>
    /// Переключить ввод указанного окна <paramref name="targetWindow"/> на язык, если он в списке,
    /// и (для японского) форсить хирагану. Цель передаётся явно: клик по трею уводит foreground
    /// на shell, поэтому <see cref="NativeMethods"/>.GetForegroundWindow в момент действия
    /// указывает не на редактор пользователя — нужно последнее пользовательское окно (трекер).
    /// Best-effort: WM_INPUTLANGCHANGEREQUEST + форс режима на сфокусированном окне целевого потока.
    /// </summary>
    void SwitchTo(LanguageProfile profile, IntPtr targetWindow);

    /// <summary>
    /// Активен ли сейчас японский на потоке окна <paramref name="window"/> (по низкому слову HKL).
    /// Дёшево и без блокировок — для триггеров форса хираганы (фокус/опрос).
    /// </summary>
    bool IsJapaneseActive(IntPtr window);

    /// <summary>
    /// Форсировать японский режим ввода (open + хирагана) на сфокусированном окне потока
    /// <paramref name="targetWindow"/>. В отличие от <see cref="SwitchTo"/> НЕ переключает язык —
    /// применяется, когда японский уже активен (вход фокуса в JP-окно, смена языка «на месте»).
    /// </summary>
    void ForceJapaneseMode(IntPtr targetWindow);
}
