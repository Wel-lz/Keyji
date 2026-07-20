using System.IO;
using System.Text.Json;

namespace Keyji.Settings;

/// <summary>
/// Загрузка/сохранение <see cref="KeyjiSettings"/> в %AppData%\Keyji\settings.json.
/// Любая ошибка чтения (нет файла / повреждён / недоступен) → дефолт, без падения.
/// </summary>
public static class SettingsService
{
    private static readonly string DirPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Keyji");

    private static readonly string FilePath = Path.Combine(DirPath, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// Файл настроек уже существует? Признак «первого запуска»: false — значит Keyji
    /// запускается впервые (онбординг). Читать ДО первого <see cref="Save"/> —
    /// конструктор <see cref="MainWindow"/> создаёт файл сразу, затирая этот сигнал.
    /// </summary>
    public static bool Exists() => File.Exists(FilePath);

    public static KeyjiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<KeyjiSettings>(File.ReadAllText(FilePath), Options);
                // Валиден, если есть хоть один язык в любой из партиций: пользователь
                // мог пометить все языки постоянными — набор пуст, но файл настоящий.
                if (settings is not null &&
                    (settings.ManagedLanguages.Count > 0 || settings.PermanentLanguages.Count > 0))
                    return settings;
            }
        }
        catch
        {
            // повреждён/недоступен — тихо откатываемся к дефолту
        }

        return KeyjiSettings.CreateDefault();
    }

    public static void Save(KeyjiSettings settings)
    {
        Directory.CreateDirectory(DirPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
    }
}
