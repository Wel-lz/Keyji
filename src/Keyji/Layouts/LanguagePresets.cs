namespace Keyji.Layouts;

/// <summary>
/// Курируемые пресеты языков. <see cref="Japanese"/> — флагман стартового
/// набора онбординга. <see cref="Catalog"/> — библиотека «+ добавить язык»:
/// флагманские мировые языки, которые можно предложить, даже если пакет не установлен.
///
/// <para><b>Контракт gated:</b> клик по неустановленному языку в каталоге ведёт
/// ТОЛЬКО в параметры Windows (deep-link) и НЕ кладёт язык в набор. Поэтому
/// <see cref="LanguageProfile.ProfileId"/> записи каталога <b>не актуируется</b>, пока
/// язык не установлен: имя строится от <c>BcpTag</c> (<see cref="LanguageNames"/>), дедуп
/// с установленным каталогом идёт по <c>LangId</c> (<see cref="LanguageProfile.LangId"/>),
/// а <c>AddToSet</c> зовётся лишь на зарегистрированных строках — где источник строки
/// либо реальный установленный каталог, либо KLID, поставляемый с Windows. Это снимает
/// требование выверять каждую install-строку вслепую (в т.ч. непроверяемые CJK-TIP).</para>
/// </summary>
public static class LanguagePresets
{
    /// <summary>Microsoft Japanese IME (TIP). LangID 0x0411.</summary>
    public static readonly LanguageProfile Japanese = new(
        LangId: 0x0411,
        ProfileId: "{03B5835F-F03C-411B-9CE2-AA23E1171E36}{A76C93D9-5523-4E90-AAFA-4DB112F9AC76}",
        IsTip: true,
        DisplayName: "日本語 — Microsoft IME",
        // Windows кладёт ветку User Profile как нейтральный "ja" (не "ja-JP") —
        // verified write-экспериментом. BcpTag не входит в
        // install-строку, но join «установленное ↔ пресет» и показ тега сломались бы.
        BcpTag: "ja");

    /// <summary>Все встроенные пресеты для стартового набора онбординга.</summary>
    public static readonly IReadOnlyList<LanguageProfile> All = new[] { Japanese };

    /// <summary>
    /// Флагманская библиотека для «+ добавить язык»: ~12 мировых языков.
    /// KLID-раскладки латиницы/кириллицы по конвенции <c>0000&lt;langid&gt;</c> (verified
    /// на живом реестре для en/ru: <c>0409:00000409</c>, <c>0419:00000419</c>), поставляются
    /// с Windows → зарегистрированы всегда → путь «+ добавить». Исключение — испанский:
    /// его раскладка живёт под KLID <c>0000040A</c>, а не <c>00000C0A</c>.
    /// CJK-TIP (кроме флагманского JP) без установленного пакета уходят по пути «скачать»;
    /// их <c>ProfileId</c> пуст — под gated он не нужен (см. контракт класса), а если пакет
    /// установлен, дедуп по LangId предпочтёт реальную запись из <see cref="InstalledCatalog"/>.
    /// </summary>
    public static readonly IReadOnlyList<LanguageProfile> Catalog = new[]
    {
        Japanese,
        new LanguageProfile(0x0409, "00000409", false, "English (US)", "en-US"),
        new LanguageProfile(0x0419, "00000419", false, "Русский",      "ru-RU"),
        new LanguageProfile(0x0407, "00000407", false, "Deutsch",      "de-DE"),
        new LanguageProfile(0x040C, "0000040C", false, "Français",     "fr-FR"),
        // Испанский: раскладка = 0000040A (не 00000C0A) — конвенция 0000<langid> здесь не держит.
        new LanguageProfile(0x0C0A, "0000040A", false, "Español",      "es-ES"),
        new LanguageProfile(0x0410, "00000410", false, "Italiano",     "it-IT"),
        new LanguageProfile(0x0416, "00000416", false, "Português",    "pt-BR"),
        new LanguageProfile(0x0413, "00000413", false, "Nederlands",   "nl-NL"),
        new LanguageProfile(0x0415, "00000415", false, "Polski",       "pl-PL"),
        new LanguageProfile(0x0401, "00000401", false, "العربية",       "ar-SA"),
        // CJK-TIP: ProfileId пуст — под gated не актуируется (deep-link «скачать»); реальный
        // идентификатор берётся из реестра при установке, дедуп с ним идёт по LangId.
        new LanguageProfile(0x0804, "", true, "中文",   "zh-CN"),
        new LanguageProfile(0x0412, "", true, "한국어", "ko-KR"),
    };
}
