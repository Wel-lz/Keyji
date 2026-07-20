using System.Globalization;

namespace Keyji.Layouts;

/// <summary>
/// Загруженная раскладка (HKL), полученная из GetKeyboardLayoutList.
/// Отражает то, что реально стоит в пользовательском списке сейчас.
/// </summary>
/// <param name="Hkl">Дескриптор раскладки.</param>
/// <param name="LangId">Language ID (младшее слово HKL).</param>
/// <param name="DisplayName">Имя культуры для UI (напр. "日本語 (Япония)").</param>
public sealed record LayoutInfo(IntPtr Hkl, ushort LangId, string DisplayName)
{
    internal static LayoutInfo FromHkl(IntPtr hkl)
    {
        var langId = (ushort)(hkl.ToInt64() & 0xFFFF);
        string name;
        try
        {
            name = CultureInfo.GetCultureInfo(langId).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            name = $"LangID 0x{langId:X4}";
        }
        return new LayoutInfo(hkl, langId, name);
    }
}
