using System.Globalization;
using System.Text;
using Keyji.Native;
using Microsoft.Win32;

namespace Keyji.Layouts;

/// <summary>
/// Каталог языков, <b>установленных</b> в системе (стоящих в списке пользователя).
/// Источник истины — ветка
/// <c>HKCU\Control Panel\International\User Profile\&lt;bcp47&gt;</c>, где имена значений
/// вида <c>&lt;langid&gt;:&lt;profileid&gt; = 1</c> — это в точности install-строки
/// (<see cref="LanguageProfile.ToInstallString"/>). Отдаёт то, что <b>уже активно</b>
/// у пользователя; не-активные, но нужные языки (флагманский японский) приходят
/// отдельно — курируемым пресетом (<see cref="LanguagePresets"/>). Полный каталог
/// неустановленных не энумерируем (публичного API нет — только deep-link).
/// </summary>
public interface IInstalledCatalog
{
    /// <summary>Установленные языки/IME как <see cref="LanguageProfile"/> (KLID + TIP).</summary>
    IReadOnlyList<LanguageProfile> GetInstalledLanguages();
}

/// <inheritdoc cref="IInstalledCatalog"/>
public sealed class InstalledCatalog : IInstalledCatalog
{
    private const string UserProfilePath = @"Control Panel\International\User Profile";

    public IReadOnlyList<LanguageProfile> GetInstalledLanguages()
    {
        using var root = Registry.CurrentUser.OpenSubKey(UserProfilePath);
        if (root is null)
            return Array.Empty<LanguageProfile>();

        var result = new List<LanguageProfile>();
        foreach (var bcpTag in root.GetSubKeyNames())
        {
            using var langKey = root.OpenSubKey(bcpTag);
            if (langKey is null)
                continue;

            foreach (var valueName in langKey.GetValueNames())
            {
                if (!TryParseInstallEntry(valueName, out ushort langId, out string profileId, out bool isTip))
                    continue;

                var displayName = ResolveDisplayName(langId, profileId, isTip, bcpTag);
                result.Add(new LanguageProfile(langId, profileId, isTip, displayName, bcpTag));
            }
        }

        return result;
    }

    /// <summary>
    /// Распарсить имя значения <c>&lt;langid&gt;:&lt;profileid&gt;</c>. Прочие значения ветки
    /// (напр. CachedLanguageName — без ':') отсеиваются проверкой формата.
    /// </summary>
    private static bool TryParseInstallEntry(string valueName, out ushort langId, out string profileId, out bool isTip)
    {
        langId = 0;
        profileId = string.Empty;
        isTip = false;

        int colon = valueName.IndexOf(':');
        if (colon <= 0 || colon == valueName.Length - 1)
            return false;

        if (!ushort.TryParse(valueName.AsSpan(0, colon), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out langId))
            return false;

        profileId = valueName[(colon + 1)..];
        isTip = profileId.StartsWith('{');
        return true;
    }

    /// <summary>
    /// Имя для UI. База — локализованное имя языка (GetLocaleInfoEx). Для TIP добавляем
    /// Description из CTF (напр. "日本語 — Microsoft IME"). Полноценную композицию
    /// «язык + IME» финализирует UI — здесь достаточно осмысленного имени.
    /// </summary>
    private static string ResolveDisplayName(ushort langId, string profileId, bool isTip, string bcpTag)
    {
        string languageName = GetLanguageName(bcpTag);
        if (string.IsNullOrEmpty(languageName))
            languageName = bcpTag;

        if (!isTip)
            return languageName;

        string? description = ResolveTipDescription(langId, profileId);
        return string.IsNullOrEmpty(description) ? languageName : $"{languageName} — {description}";
    }

    private static string GetLanguageName(string bcpTag)
    {
        var buffer = new StringBuilder(256);
        int len = NativeMethods.GetLocaleInfoEx(
            bcpTag, NativeMethods.LOCALE_SLOCALIZEDDISPLAYNAME, buffer, buffer.Capacity);
        return len > 0 ? buffer.ToString() : string.Empty;
    }

    /// <summary>
    /// Description TIP из <c>HKLM\...\CTF\TIP\{CLSID}\LanguageProfile\0x0000&lt;langid&gt;\{GUID}</c>.
    /// profileid TIP = "{CLSID}{GUID}" — две склеенные GUID-строки по 38 символов.
    /// </summary>
    private static string? ResolveTipDescription(ushort langId, string profileId)
    {
        const int guidLength = 38; // "{8-4-4-4-12}"
        if (profileId.Length < guidLength * 2)
            return null;

        string clsid = profileId[..guidLength];
        string guid = profileId[guidLength..];
        string path = $@"SOFTWARE\Microsoft\CTF\TIP\{clsid}\LanguageProfile\0x0000{langId:x4}\{guid}";

        using var key = Registry.LocalMachine.OpenSubKey(path);
        if (key?.GetValue("Description") is not string raw || raw.Length == 0)
            return null;

        return ResolveIndirect(raw);
    }

    /// <summary>Разрешить "@dll,-N" через SHLoadIndirectString; литерал возвращаем как есть.</summary>
    private static string ResolveIndirect(string raw)
    {
        if (!raw.StartsWith('@'))
            return raw;

        var buffer = new StringBuilder(512);
        int hr = NativeMethods.SHLoadIndirectString(raw, buffer, buffer.Capacity, IntPtr.Zero);
        return hr == 0 && buffer.Length > 0 ? buffer.ToString() : raw;
    }
}
