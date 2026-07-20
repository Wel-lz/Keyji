namespace Keyji.Panel;

/// <summary>
/// Пункт «…»-меню строки. Состав меню зависит от <see cref="PanelRowKind"/> и
/// собирается в <c>PanelWindow.BuildMenuItems</c>; сюда попадает готовый пункт вместе
/// с доменным действием, которое исполняется по клику.
/// </summary>
/// <param name="Text">Подпись пункта. Формулировки заморожены.</param>
/// <param name="Invoke">Действие домена пункта (закрепить / сделать системным / убрать из набора).</param>
/// <param name="IsDestructive">
/// Деструктивный пункт («Убрать из набора»): рисуется danger-цветом и отбивается
/// разделителем от остальных («внизу, отделён разделителем»).
/// </param>
/// <param name="IsChecked">
/// Чекбокс-пункт (тогл-настройка вроде «Форсировать хирагану» на японской строке):
/// <c>true</c> — галочка стоит, <c>false</c> — пункт чекаемый, но снят (место под галочку
/// зарезервировано), <c>null</c> — обычный пункт-действие (галочки нет, места не занимает).
/// </param>
public sealed record PanelMenuItemView(string Text, Action Invoke, bool IsDestructive = false, bool? IsChecked = null);
