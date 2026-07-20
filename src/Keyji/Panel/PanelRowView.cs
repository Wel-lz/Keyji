using System.ComponentModel;
using System.Runtime.CompilerServices;
using Keyji.Layouts;

namespace Keyji.Panel;

/// <summary>Группа, в которой строка живёт на панели.</summary>
public enum PanelRowKind
{
    /// <summary>Закреплённый язык — единственный, в accent-подложке, с пин-глифом.</summary>
    Pinned,

    /// <summary>Обычный управляемый язык — тогл вкл/выкл (или «скачать», если не зарегистрирован).</summary>
    Normal,

    /// <summary>Системный (в коде — «постоянный») язык: статичен, только бейдж и «…».</summary>
    System,
}

/// <summary>
/// Строка панели: пара имён + состояние, которого хватает вёрстке, чтобы выбрать
/// правый блок (тогл / бейдж / «скачать»), плюс доменный <see cref="Profile"/> —
/// язык, к которому строка привязана. Действия строки (тогл, «…»-меню) читают
/// <see cref="Profile"/>, чтобы дёрнуть глаголы домена на нужном языке.
/// </summary>
public sealed class PanelRowView : INotifyPropertyChanged
{
    private bool _isOn;

    /// <summary>Язык этой строки. Заполняется при построении из набора.</summary>
    public required LanguageProfile Profile { get; init; }

    /// <summary>Нативный эндоним (Georgia): 日本語 / Русский.</summary>
    public required string Native { get; init; }

    /// <summary>Локализованное имя от ОС (умно сокращённое) + IME-дескриптор (Space Mono, small-caps): JAPANESE · IME.</summary>
    public required string Label { get; init; }

    /// <summary>Полное имя от ОС для тултипа, если <see cref="Label"/> был сокращён; иначе <c>null</c>.</summary>
    public string? Tooltip { get; init; }

    public PanelRowKind Kind { get; init; } = PanelRowKind.Normal;

    /// <summary>Раскладка не установлена на машине — вместо тогла кнопка «⤓ скачать».</summary>
    public bool Downloadable { get; init; }

    /// <summary>Язык включён в Windows прямо сейчас. Тогл биндится сюда TwoWay.</summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value)
                return;
            _isOn = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
