using System.Diagnostics;
using System.IO;

namespace Keyji.Diagnostics;

/// <summary>
/// Лёгкий дозаписываемый лог для самой хрупкой части — форса режима IME.
/// Цель форса (какое окно на переднем плане в момент триггера) — load-bearing риск:
/// если это shell/taskbar/само окно Keyji, а не редактор пользователя, SET уходит в
/// чужой input-контекст и живой ввод молча не меняется. Этот trace фиксирует фактическую
/// цель, чтобы верификацию можно было свести с GET-readback (недостаточен) на факт.
/// Любая ошибка записи проглатывается — диагностика не должна ронять приложение.
///
/// Только для DEBUG-сборок: <see cref="Line"/> помечен <see cref="ConditionalAttribute"/>("DEBUG"),
/// поэтому в Release компилятор вырезает сами вызовы — де-рискинг форса завершён, у релиза
/// этого лога быть не должно.
/// </summary>
public static class Trace
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Keyji", "ime-switch.log");

    [Conditional("DEBUG")]
    public static void Line(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch
        {
            // Диагностика — best-effort; никогда не мешаем основному потоку.
        }
    }
}
