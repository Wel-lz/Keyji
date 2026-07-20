namespace Keyji.Layouts;

/// <summary>
/// Язык/раскладка, которым Keyji управляет в пользовательском списке.
/// Несёт идентификатор единообразно для обычной раскладки (KLID) и для TIP/IME
/// ({CLSID}{GUID}) — именно эта строка уходит в InstallLayoutOrTip.
/// </summary>
/// <param name="LangId">Language ID, напр. 0x0411 для японского.</param>
/// <param name="ProfileId">
/// Часть строки после "LangID:": KLID ("00000409") для обычной раскладки
/// либо "{CLSID}{GUID}" для TIP.
/// </param>
/// <param name="IsTip">true — text input processor (IME), false — обычная раскладка.</param>
/// <param name="DisplayName">Человекочитаемое имя для UI/трея.</param>
/// <param name="BcpTag">BCP-47 тег, напр. "ja-JP".</param>
public sealed record LanguageProfile(
    ushort LangId,
    string ProfileId,
    bool IsTip,
    string DisplayName,
    string BcpTag)
{
    /// <summary>Строка для InstallLayoutOrTip: "LangID:ProfileId" (langid в hex, 4 знака).</summary>
    public string ToInstallString() => $"{LangId:x4}:{ProfileId}";
}
