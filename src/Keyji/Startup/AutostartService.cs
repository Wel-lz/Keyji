using Microsoft.Win32;

namespace Keyji.Startup;

/// <summary>
/// Автозапуск через запись в HKCU\...\Run (unelevated, per-user).
/// v0.1 распространяется одним .exe без установщика — поэтому в UI обязательна
/// подпись-предупреждение: сначала выключить автозапуск, потом удалять .exe.
/// </summary>
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Keyji";

    /// <summary>Путь к текущему .exe (для single-file — сам исполняемый файл).</summary>
    private static string ExecutablePath =>
        Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь к процессу.");

    /// <summary>Включён ли автозапуск и указывает ли он на текущий .exe.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        // Значение вида: "C:\...\Keyji.exe" --tray — проверяем, что путь наш.
        return key?.GetValue(ValueName) is string value
            && value.Contains($"\"{ExecutablePath}\"", StringComparison.OrdinalIgnoreCase);
    }

    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        // Автозапуск при входе — свёрнутым в трей, без окна онбординга.
        key.SetValue(ValueName, $"\"{ExecutablePath}\" {App.TrayArg}");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(ValueName) is not null)
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
