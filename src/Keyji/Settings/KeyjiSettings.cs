using System.Text.Json.Serialization;
using Keyji.Layouts;
using Keyji.Localization;
using Keyji.Theming;

namespace Keyji.Settings;

/// <summary>
/// Персистентные настройки Keyji. Храним ТОЛЬКО состав управляемого набора языков
/// и выбор закреплённого. Состояние вкл/выкл НЕ храним — читаем живьём из системы
/// (<see cref="ILayoutService.IsEnabled"/>), иначе два источника правды разъедутся,
/// как только пользователь включит язык через настройки Windows мимо Keyji.
/// </summary>
public sealed class KeyjiSettings
{
    /// <summary>Языки, которыми управляет пользователь (его набор).</summary>
    public List<LanguageProfile> ManagedLanguages { get; set; } = new();

    /// <summary>
    /// Постоянные (системные) языки — Keyji их <b>никогда</b> не выключает и не убирает.
    /// Это базовые раскладки, стоявшие до Keyji (English/русский и т.п.): выключить их
    /// значило бы оставить пользователя без ввода. Партиция с <see cref="ManagedLanguages"/>
    /// по install-строке (язык — либо управляемый, либо постоянный, не оба). Сид — на
    /// первом запуске (каталог − управляемые), дальше метится/снимается из трей-меню.
    /// </summary>
    public List<LanguageProfile> PermanentLanguages { get; set; } = new();

    /// <summary>Ключ закреплённого языка = его install-строка ("LangID:ProfileId").</summary>
    public string? PinnedKey { get; set; }

    /// <summary>
    /// Выбор темы панели. Сериализуется строкой (<c>System|Light|Dark</c>)
    /// — атрибут скоупит string-enum на это поле, не трогая формат остального графа. Поля нет
    /// в старом файле → дефолт <see cref="ThemePreference.System"/>.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>
    /// Выбор языка UI. Строка: <c>"System"</c> (<see cref="Loc.SystemPreference"/>,
    /// следовать ОС) либо код культуры <c>ru</c>/<c>en</c>/<c>ja</c>. Поля нет в старом файле →
    /// дефолт <c>"System"</c> (поведение <see cref="Loc.Initialize"/> сохранено).
    /// </summary>
    public string Language { get; set; } = Loc.SystemPreference;

    /// <summary>
    /// Форсировать хирагану при переключении на японский (флагманская фича). Когда включено,
    /// Keyji выставляет open + режим хираганы каждый раз, как японский становится активным —
    /// при входе фокуса в JP-окно и при смене языка «на месте» (Shift+Tab/Win+Space), не
    /// только по собственному тоглу из трея. Тогл — пункт «…»-меню японской строки. Дефолт —
    /// включено (в этом флагманский смысл Keyji для изучающих японский). Поля нет в старом
    /// файле → дефолт <c>true</c>.
    /// </summary>
    public bool ForceHiraganaOnJapanese { get; set; } = true;

    /// <summary>Дефолт для первого запуска: японский пресет, он же закреплённый.</summary>
    public static KeyjiSettings CreateDefault() => new()
    {
        ManagedLanguages = new List<LanguageProfile> { LanguagePresets.Japanese },
        PinnedKey = LanguagePresets.Japanese.ToInstallString(),
    };

    /// <summary>Язык уже в наборе? Идентичность — по install-строке (без учёта регистра).</summary>
    public bool Contains(LanguageProfile profile) =>
        ManagedLanguages.Any(l => KeyEquals(l, profile));

    /// <summary>Язык помечен постоянным (Keyji его не трогает)? Идентичность — по install-строке.</summary>
    public bool IsPermanent(LanguageProfile profile) =>
        PermanentLanguages.Any(l => KeyEquals(l, profile));

    /// <summary>
    /// add-to-set: положить язык в набор. Идемпотентно — дубликат по install-строке
    /// не добавляется. <b>Не</b> включает язык в Windows (enable — отдельный глагол
    /// <see cref="ILayoutService.EnableLanguage"/>). Персистентность — за вызывающим
    /// (<see cref="SettingsService.Save"/>).
    /// </summary>
    /// <returns>true, если язык добавлен; false, если уже был в наборе.</returns>
    public bool AddToSet(LanguageProfile profile)
    {
        if (Contains(profile) || IsPermanent(profile))
            return false;

        ManagedLanguages.Add(profile);
        return true;
    }

    /// <summary>
    /// Пометить управляемый язык постоянным: переносим из набора в <see cref="PermanentLanguages"/>.
    /// Keyji перестаёт его включать/выключать/убирать. Если это был закреплённый —
    /// <see cref="PinnedKey"/> обнуляется: закреп опционален, нового закрепа
    /// автоматически НЕ избираем (симметрия с <see cref="RemoveFromSet"/>).
    /// Набор может опустеть — гардрейл «нельзя убрать последний» снят.
    /// Персистентность — за вызывающим.
    /// </summary>
    /// <returns>true, если язык был управляемым и стал постоянным.</returns>
    public bool MarkPermanent(LanguageProfile profile)
    {
        int index = ManagedLanguages.FindIndex(l => KeyEquals(l, profile));
        if (index < 0 || IsPermanent(profile))
            return false;

        var lang = ManagedLanguages[index];
        bool wasPinned = KeyMatches(PinnedKey, lang);
        ManagedLanguages.RemoveAt(index);
        PermanentLanguages.Add(lang);

        if (wasPinned)
            PinnedKey = null;

        return true;
    }

    /// <summary>
    /// Снять постоянный статус: вернуть язык из <see cref="PermanentLanguages"/> в управляемый
    /// набор (снова доступен для enable/disable/remove). Персистентность — за вызывающим.
    /// </summary>
    /// <returns>true, если язык был постоянным и возвращён в набор.</returns>
    public bool UnmarkPermanent(LanguageProfile profile)
    {
        int index = PermanentLanguages.FindIndex(l => KeyEquals(l, profile));
        if (index < 0)
            return false;

        var lang = PermanentLanguages[index];
        PermanentLanguages.RemoveAt(index);
        if (!Contains(lang))
            ManagedLanguages.Add(lang);

        return true;
    }

    /// <summary>
    /// Убрать язык из постоянных <b>совсем</b> (не возвращая в набор): язык остаётся
    /// установленным, но Keyji его не трогает и предлагает в «Добавить». Для пикера
    /// первого запуска (снятие галочки «постоянный»). Персистентность — за вызывающим.
    /// </summary>
    /// <returns>true, если язык был постоянным и снят.</returns>
    public bool DropPermanent(LanguageProfile profile)
    {
        int index = PermanentLanguages.FindIndex(l => KeyEquals(l, profile));
        if (index < 0)
            return false;

        PermanentLanguages.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// remove-from-set: убрать язык <b>только из набора Keyji</b>. В Windows язык
    /// НЕ выключаем (симметрия add-to-set≠enable, принцип «вкл/выкл читаем живьём»).
    /// Если убираем закреплённый — <see cref="PinnedKey"/> обнуляется: закреп
    /// опционален, нового закрепа автоматически НЕ избираем и набор может
    /// опустеть (гардрейл «нельзя убрать последний» снят). Персистентность — за вызывающим.
    /// </summary>
    /// <returns>true, если язык был в наборе и убран.</returns>
    public bool RemoveFromSet(LanguageProfile profile)
    {
        int index = ManagedLanguages.FindIndex(l => KeyEquals(l, profile));
        if (index < 0)
            return false;

        bool wasPinned = KeyMatches(PinnedKey, ManagedLanguages[index]);
        ManagedLanguages.RemoveAt(index);

        if (wasPinned)
            PinnedKey = null;

        return true;
    }

    private static bool KeyEquals(LanguageProfile a, LanguageProfile b) =>
        string.Equals(a.ToInstallString(), b.ToInstallString(), StringComparison.OrdinalIgnoreCase);

    private static bool KeyMatches(string? installKey, LanguageProfile profile) =>
        string.Equals(installKey, profile.ToInstallString(), StringComparison.OrdinalIgnoreCase);
}
