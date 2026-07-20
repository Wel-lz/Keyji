using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Data;

namespace Keyji.Localization;

/// <summary>
/// Провайдер локализации UI. Плоский key→value словарь на язык, лежит
/// embedded-JSON внутри single-file exe (без сателлитных сборок и без loose-файлов
/// рядом с exe — все языки в бинаре). Строки тянут и XAML через <c>{loc:T Key}</c>
/// (<see cref="TExtension"/>), и code-behind через <see cref="Get(string)"/>.
///
/// Культура выбирается настройкой: дефолт <c>"System"</c> = культура
/// ОС на старте (<see cref="Initialize"/>), явные <c>ru</c>/<c>en</c>/<c>ja</c> фиксируют язык.
/// Выбор применяется на лету через <see cref="ApplyPreference"/> → <see cref="SetCulture"/>:
/// активный словарь перегружается и будит <see cref="INotifyPropertyChanged"/>, чтобы XAML-биндинги
/// <c>{loc:T}</c> перечитались; строки, собранные в code-behind (строки панели, трей-тултип),
/// перечитывает вызывающий (оверлей настроек). Персист — поле <c>Language</c> в settings.json.
///
/// Отсутствующий ключ падает на английское значение (частичный перевод не ломает UI);
/// если и в английском нет — возвращается сам ключ (видимый маркер незаполненной строки).
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>Языки, поставляемые в v0.1 (embedded). Остальные приносит сообщество через пересборку.</summary>
    private static readonly string[] Supported = { "ru", "en", "ja" };

    private const string Fallback = "en";

    /// <summary>Сентинел настройки «следовать языку ОS»: не код культуры, а «System» в settings.json.</summary>
    public const string SystemPreference = "System";

    /// <summary>Singleton — источник и для XAML-биндингов (индексатор), и для статического <see cref="Get(string)"/>.</summary>
    public static Loc Instance { get; } = new();

    /// <summary>Английский словарь всегда загружен: fallback для отсутствующих ключей активного языка.</summary>
    private readonly Dictionary<string, string> _fallback = Load(Fallback);

    private Dictionary<string, string> _active;

    private Loc() => _active = _fallback;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Активный код языка (`ru`/`en`/`ja`) после разрешения настройки.</summary>
    public string CultureCode { get; private set; } = Fallback;

    /// <summary>
    /// Baseline на старте: культура ОС (<see cref="CultureInfo.CurrentUICulture"/>), fallback на
    /// английский. Зовётся в <c>App.OnStartup</c> ДО создания любого UI — иначе первый кадр покажет
    /// чужой язык. Сохранённый выбор накатывает поверх <c>MainWindow</c> через <see cref="ApplyPreference"/>.
    /// </summary>
    public static void Initialize() => Instance.Apply(OsCulture());

    /// <summary>Код языка ОС, если поддержан (embedded), иначе английский fallback.</summary>
    private static string OsCulture()
    {
        string osLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return System.Array.IndexOf(Supported, osLang) >= 0 ? osLang : Fallback;
    }

    /// <summary>
    /// Применить выбор языка из настройки: <see cref="SystemPreference"/> → культура ОС,
    /// явный код → он сам. Live через <see cref="SetCulture"/>. Персист (запись <c>Language</c>) и
    /// перечитывание code-behind строк — за вызывающим (оверлей настроек).
    /// </summary>
    public void ApplyPreference(string preference)
        => SetCulture(preference == SystemPreference ? OsCulture() : preference);

    /// <summary>
    /// Сменить активный язык на лету. Перечитывает словарь и будит XAML-биндинги через
    /// <c>Item[]</c>; code-behind строки (панель, трей) перечитывает вызывающий. No-op, если код
    /// уже активен.
    /// </summary>
    public void SetCulture(string code)
    {
        if (code == CultureCode)
            return;
        Apply(code);
        // Индексаторные биндинги ({loc:T}) обновляются по нотификации "Item[]".
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }

    private void Apply(string code)
    {
        CultureCode = code;
        _active = code == Fallback ? _fallback : Load(code);
    }

    /// <summary>Индексатор для XAML-биндинга <c>{loc:T Key}</c> (см. <see cref="TExtension"/>).</summary>
    public string this[string key] => Get(key);

    /// <summary>Строка по ключу: активный язык → английский fallback → сам ключ (маркер пропуска).</summary>
    public static string Get(string key)
    {
        if (Instance._active.TryGetValue(key, out var value))
            return value;
        if (Instance._fallback.TryGetValue(key, out var en))
            return en;
        return key;
    }

    /// <summary>Форматная строка по ключу с подстановкой аргументов (<c>string.Format</c>).</summary>
    public static string Get(string key, params object?[] args)
        => string.Format(Get(key), args);

    /// <summary>
    /// Прочитать embedded-JSON языка в плоский словарь. Ресурс ищется по суффиксу
    /// <c>.{code}.json</c>, чтобы не завязываться на точное имя namespace-пути манифеста.
    /// </summary>
    private static Dictionary<string, string> Load(string code)
    {
        var asm = Assembly.GetExecutingAssembly();
        string suffix = $".{code}.json";
        string? name = System.Array.Find(asm.GetManifestResourceNames(), n => n.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase));
        if (name is null)
            return new Dictionary<string, string>();

        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        string json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
    }
}
