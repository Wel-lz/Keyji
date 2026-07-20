using System.Globalization;
using System.Text;
using Keyji.Native;

namespace Keyji.Layouts;

/// <summary>
/// Пара имён языка для строки панели: нативный эндоним + локализованное имя от ОС.
/// Оба имени берутся из <see cref="NativeMethods.GetLocaleInfoEx"/> по BCP-47 тегу, а НЕ
/// из таблицы переводов i18n (i18n владеет строками
/// UI, имена языков — от ОС). Собирается на лету при построении строк, модель
/// <see cref="LanguageProfile"/> пары не хранит.
/// </summary>
public static class LanguageNames
{
    /// <summary>
    /// (эндоним, лейбл, тултип) для строки: эндоним — sans (日本語 / Русский); лейбл —
    /// локализованное имя ОС в верхнем регистре под Space Mono, **умно сокращённое** (имя
    /// языка + первое уточнение, шумные хвосты отброшены — см. <see cref="Shorten"/>), с
    /// маркером «· IME» для TIP; тултип — полное имя ОС, но только если сокращение что-то
    /// отбросило (иначе <c>null</c> → тултипа нет). Имена ОС бывают многословны
    /// («испанский (Испания, международная сортировка)»), лейбл держим коротким, детали — в тултипе.
    /// </summary>
    public static (string Native, string Label, string? Tooltip) Resolve(LanguageProfile profile)
    {
        string native = Capitalize(Lookup(profile.BcpTag, NativeMethods.LOCALE_SNATIVELANGUAGENAME, profile.BcpTag));
        string localizedFull = Lookup(profile.BcpTag, NativeMethods.LOCALE_SLOCALIZEDDISPLAYNAME, profile.BcpTag);

        var (shortName, dropped) = Shorten(localizedFull);

        string label = shortName.ToUpper(CultureInfo.CurrentCulture);
        if (profile.IsTip)
            label += " · IME";

        // Тултип — полное имя ОС, только когда сокращение реально что-то отбросило: иначе
        // лейбл == полному имени, и тултип был бы бессмысленным дублем.
        string? tooltip = dropped ? Capitalize(localizedFull) : null;

        return (native, label, tooltip);
    }

    /// <summary>
    /// Сократить локализованное имя ОС до «язык (одно уточнение)», отбросив шум. Windows
    /// отдаёт два непоследовательных формата (проверено на живой ОС):
    /// <list type="bullet">
    /// <item>A — <c>Язык (регион[, сортировка])</c>: «Испанский (Испания, международная
    /// сортировка)» → «Испанский (Испания)» (регион нужен, сортировка — шум);</item>
    /// <item>B — <c>Язык, вариант (регион)</c>: «Китайский, упрощенное письмо (континентальный
    /// Китай)» → «Китайский (упрощенное письмо)» (вариант письма нужен, регион — избыточен).</item>
    /// </list>
    /// Различаем по положению первой запятой: если она ДО открывающей скобки — формат B.
    /// Без скобок и запятой имя возвращается как есть.
    /// </summary>
    /// <returns>(сокращённое имя, был ли отброшен хвост → нужен ли тултип с полным именем).</returns>
    private static (string Short, bool Dropped) Shorten(string name)
    {
        int open = name.IndexOf('(');
        int comma = name.IndexOf(',');

        // Формат B: «Язык, вариант (регион)» — запятая раньше скобки. Берём язык + вариант
        // (между запятой и скобкой), отбрасываем содержимое скобки (регион избыточен).
        if (comma >= 0 && (open < 0 || comma < open))
        {
            string head = name[..comma].TrimEnd();
            int variantEnd = open > comma ? open : name.Length;
            string variant = name[(comma + 1)..variantEnd].Trim();
            string result = variant.Length == 0 ? head : $"{head} ({variant})";
            // Отбросили, если была скобка-регион или ещё запятые в варианте.
            bool dropped = open >= 0 || variant.Contains(',');
            return (result, dropped);
        }

        // Формат A: «Язык (уточнение1[, уточнение2…])» — оставляем первое уточнение.
        if (open < 0)
            return (name, false);

        int close = name.IndexOf(')', open);
        string langHead = name[..open].TrimEnd();
        string inside = close > open ? name[(open + 1)..close] : name[(open + 1)..];

        var quals = inside.Split(',');
        string first = quals[0].Trim();
        bool droppedTail = quals.Length > 1;

        string shortName = first.Length == 0 ? langHead : $"{langHead} ({first})";
        return (shortName, droppedTail);
    }

    /// <summary>
    /// Чистое имя языка для поиска в пикере Windows: локализованное имя без скобочных
    /// и запятых-уточнений — «Китайский», «Испанский», «Корейский». Кладётся в буфер обмена
    /// при «скачать», чтобы вставить в поиск диалога «Выбор языка для установки»: пикер
    /// нельзя открыть/предзаполнить deep-link'ом (проверено на Win11), а его поиск матчит
    /// имена на языке интерфейса.
    /// </summary>
    public static string SearchTerm(LanguageProfile profile)
    {
        string localized = Lookup(profile.BcpTag, NativeMethods.LOCALE_SLOCALIZEDDISPLAYNAME, profile.BcpTag);
        int cut = localized.IndexOfAny(new[] { '(', ',' });
        return (cut > 0 ? localized[..cut] : localized).Trim();
    }

    private static string Lookup(string bcpTag, uint lcType, string fallback)
    {
        var buffer = new StringBuilder(256);
        int len = NativeMethods.GetLocaleInfoEx(bcpTag, lcType, buffer, buffer.Capacity);
        return len > 0 ? buffer.ToString() : fallback;
    }

    /// <summary>
    /// Первая буква — в верхний регистр. Нативный эндоним ОС отдаёт по конвенции языка
    /// (русский — со строчной «русский»), а отображать нужно «Русский»; приводим к
    /// этому виду. Для CJK-письма без регистра — no-op.
    /// </summary>
    private static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        return char.ToUpper(s[0], CultureInfo.CurrentCulture) + s[1..];
    }
}
