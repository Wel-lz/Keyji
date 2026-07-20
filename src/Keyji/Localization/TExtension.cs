using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace Keyji.Localization;

/// <summary>
/// XAML markup extension локализации: <c>{loc:T Key}</c>. Возвращает биндинг на
/// индексатор <see cref="Loc.Instance"/>, поэтому строка перечитывается сама, когда
/// смена языка будит <c>Item[]</c> — без этого live-swap не увидел бы XAML.
///
/// Ручной минимальный extension выбран сознательно (ближе к этосу проекта: single-file свят,
/// ноль зависимостей). Санкционированный резерв — WPFLocalizeExtension, если упрёмся в
/// design-time/типизированные краевые случаи.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string key) => Key = key;

    /// <summary>Ключ строки в плоском словаре (напр. <c>panel.tooltip.exit</c>).</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
