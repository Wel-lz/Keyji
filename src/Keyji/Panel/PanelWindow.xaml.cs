using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Keyji.Layouts;
using Keyji.Localization;
using Keyji.Settings;
using Keyji.Startup;
using Keyji.Theming;

namespace Keyji.Panel;

/// <summary>
/// Кастомная панель-карта у трея — основной UI: заменяет нативное
/// ПКМ-меню (<c>MainWindow.BuildTrayMenu</c> удалён). Живой набор языков читается из
/// <see cref="KeyjiSettings"/> и <see cref="ILayoutService"/>, три глагола домена
/// (enable/disable, pin, add/remove-from-set, mark/unmark-permanent) дёргаются прямо из
/// строк и «…»-меню. После любой мутации панель пересобирает строки и зовёт
/// <see cref="_onDomainChanged"/>, чтобы хост-окно пере-разрешило закреплённый и обновило трей.
///
/// Поверхность: borderless окно, dismiss по Deactivated/Esc, вложенные
/// меню и add-оверлей — слои в ЭТОМ же HWND (не Popup).
/// </summary>
public partial class PanelWindow : Window
{
    /// <summary>
    /// Поле вокруг карты в корневом Grid — место под тень. Компенсируется при
    /// позиционировании, иначе карта висела бы в 60px от угла рабочей области.
    /// Синхронно с полем корневого Grid в XAML — оно держит тень карты без обрыва.
    /// </summary>
    private const double ShadowMargin = 60;

    /// <summary>Зазор между картой и краем рабочей области — панель не прилипает к трею вплотную.</summary>
    private const double AnchorGap = 8;

    /// <summary>Закрытие уже идёт — см. <see cref="Dismiss"/>.</summary>
    private bool _closing;

    private readonly ILayoutService _service;
    private readonly IInstalledCatalog _catalog;
    private readonly KeyjiSettings _settings;

    /// <summary>Пере-разрешить закреплённый и обновить трей в хост-окне после мутации набора.</summary>
    private readonly Action _onDomainChanged;

    /// <summary>Реальный выход из приложения (<c>MainWindow.RequestExit</c>).</summary>
    private readonly Action _onExit;

    public PanelWindow(
        ILayoutService service,
        IInstalledCatalog catalog,
        KeyjiSettings settings,
        Action onDomainChanged,
        Action onExit)
    {
        _service = service;
        _catalog = catalog;
        _settings = settings;
        _onDomainChanged = onDomainChanged;
        _onExit = onExit;

        InitializeComponent();

        BuildRows();
        LaunchOnStart.IsChecked = AutostartService.IsEnabled();
        InitThemeSegment();
        InitLanguageSelect();

        SizeChanged += (_, _) => Reposition();
        Loaded += (_, _) => PlayPopIn();
    }

    /// <summary>
    /// Показать панель, прижав карту к правому-нижнему углу монитора трея (якорь по
    /// <see cref="TrayAnchor"/>, не по WorkArea основного экрана).
    /// </summary>
    public void ShowAtTray()
    {
        // Реальный угол известен только по HWND (монитор трея, его DPI), а хэндл
        // появляется на Show. Чтобы первый кадр не мигнул посреди экрана, показываем
        // окно заведомо за пределами видимой области и сразу переставляем.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;

        Show();
        Reposition();
        Activate();
    }

    /// <summary>
    /// Держит угол карты на месте при любом изменении размера. Отсюда же берётся
    /// требование «панель растёт вверх»: раскрытие секции настроек двигает
    /// противоположный край, а список языков под курсором не уезжает.
    /// </summary>
    private void Reposition()
        => TrayAnchor.Snap(new WindowInteropHelper(this).Handle, ActualWidth, ActualHeight, ShadowMargin, AnchorGap);

    // --- Построение строк из живого набора ---

    /// <summary>
    /// Собрать строки из набора: закреплённый (совпал с <see cref="KeyjiSettings.PinnedKey"/>)
    /// в свою группу, прочие управляемые — обычные, постоянные — системные. Пустые группы
    /// схлопывает <see cref="UpdateGroupVisibility"/> (закреп опционален, набор может опустеть).
    /// </summary>
    private void BuildRows()
    {
        var pinned = new List<PanelRowView>();
        var normal = new List<PanelRowView>();
        foreach (var lang in _settings.ManagedLanguages)
        {
            bool isPinned = _settings.PinnedKey is not null && KeyEquals(lang, _settings.PinnedKey);
            (isPinned ? pinned : normal).Add(MakeRow(lang, isPinned ? PanelRowKind.Pinned : PanelRowKind.Normal));
        }

        PinnedRows.ItemsSource = pinned;
        NormalRows.ItemsSource = normal;
        SystemRows.ItemsSource = _settings.PermanentLanguages
            .Select(l => MakeRow(l, PanelRowKind.System))
            .ToList();

        UpdateGroupVisibility();
    }

    private PanelRowView MakeRow(LanguageProfile lang, PanelRowKind kind)
    {
        var (native, label, tooltip) = LanguageNames.Resolve(lang);
        bool registered = _service.IsRegisteredOnMachine(lang);
        return new PanelRowView
        {
            Profile = lang,
            Native = native,
            Label = label,
            Tooltip = tooltip,
            Kind = kind,
            // «⤓ скачать» вместо тогла — только у обычной строки без бинарей на машине.
            Downloadable = kind == PanelRowKind.Normal && !registered,
            IsOn = registered && _service.IsEnabled(lang),
        };
    }

    /// <summary>
    /// Пустая группа схлопывается целиком. Это штатный
    /// путь, а не край: закреплённого может не быть, управляемый набор может опустеть.
    /// </summary>
    private void UpdateGroupVisibility()
    {
        PinnedGroup.Visibility = GroupVisibility(PinnedRows);
        NormalRows.Visibility = GroupVisibility(NormalRows);
        SystemRows.Visibility = GroupVisibility(SystemRows);

        static Visibility GroupVisibility(ItemsControl rows)
            => rows.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Глаголы домена: мутируют набор, персистят, пересобирают строки, будят трей ---

    /// <summary>Записать настройки, пересобрать строки и обновить трей после мутации набора.</summary>
    private void Commit()
    {
        SettingsService.Save(_settings);
        BuildRows();
        _onDomainChanged();
    }

    private void Pin(LanguageProfile lang)
    {
        _settings.PinnedKey = lang.ToInstallString();
        Commit();
    }

    /// <summary>Открепить: закреп опционален, нового не избираем — набор остаётся без закрепа.</summary>
    private void Unpin()
    {
        _settings.PinnedKey = null;
        Commit();
    }

    private void MakePermanent(LanguageProfile lang)
    {
        if (_settings.MarkPermanent(lang))
            Commit();
    }

    private void MakeManaged(LanguageProfile lang)
    {
        if (_settings.UnmarkPermanent(lang))
            Commit();
    }

    private void RemoveFromSet(LanguageProfile lang)
    {
        if (_settings.RemoveFromSet(lang))
            Commit();
    }

    private void AddToSet(LanguageProfile lang)
    {
        if (_settings.AddToSet(lang))
            Commit();
    }

    /// <summary>Тумблер строки: применить enable/disable к языку, откатив вид при неудаче.</summary>
    private void OnRowToggle(object sender, RoutedEventArgs e)
    {
        var toggle = (ToggleButton)sender;
        var row = (PanelRowView)toggle.DataContext;

        // TwoWay-биндинг уже записал желаемое состояние в row.IsOn до этого события.
        bool enable = row.IsOn;
        bool ok = enable ? _service.EnableLanguage(row.Profile) : _service.DisableLanguage(row.Profile);

        if (!ok)
        {
            // Откатываем вид: тумблер щёлкает обратно (биндинг). Диалог не показываем —
            // модальное окно уронило бы активацию панели и она бы закрылась.
            row.IsOn = !enable;
            return;
        }

        // Строк не пересобираем (состав набора не изменился), но закреплённый мог сменить
        // вкл/выкл — будим трей. IsOn уже верен через биндинг.
        _onDomainChanged();
    }

    /// <summary>Раскладки нет на машине — ведём в Settings докачать её с инструкцией.</summary>
    private void OnDownload(object sender, RoutedEventArgs e)
    {
        var row = (PanelRowView)((System.Windows.FrameworkElement)sender).DataContext;
        GuideToLanguageSettings(row.Profile);
    }

    // --- «…»-меню строки ---

    /// <summary>Зазор между кебабом и меню — `top: calc(100% + 6px)`.</summary>
    private const double RowMenuGap = 6;

    /// <summary>Строка, чьё меню открыто сейчас; <c>null</c> — меню закрыто.</summary>
    private PanelRowView? _menuRow;

    private void OnRowMenu(object sender, RoutedEventArgs e)
    {
        var kebab = (FrameworkElement)sender;
        var row = (PanelRowView)kebab.DataContext;

        // Повторный клик по тому же кебабу закрывает меню. Достижим
        // только пока меню закрыто: открытое накрывает строки click-catcher'ом.
        if (ReferenceEquals(_menuRow, row))
        {
            CloseRowMenu();
            return;
        }

        // Взаимное исключение: открытый оверлей настроек закрывается.
        CloseSettings();

        _menuRow = row;
        RowMenuItems.ItemsSource = BuildMenuItems(row);
        RowMenuLayer.Visibility = Visibility.Visible;

        // Ширина/высота меню известны только после раскладки: ширина тянется по самому
        // длинному пункту (MinWidth 190), правый край прибит к правому краю кебаба.
        RowMenu.UpdateLayout();
        Point topLeft = kebab.TransformToVisual(RowMenuLayer).Transform(new Point(0, 0));
        Point bottomRight = kebab.TransformToVisual(RowMenuLayer)
            .Transform(new Point(kebab.ActualWidth, kebab.ActualHeight));

        // Округляем смещения до целого пикселя: меню сдвигается RenderTransform'ом, а его
        // UseLayoutRounding не трогает — дробная позиция давала «мыльный» текст (как у модалок,
        // где то же лечит UseLayoutRounding).
        MenuOffset.X = Math.Round(bottomRight.X - RowMenu.ActualWidth);

        // Вертикальный clamp: меню позиционируется RenderTransform'ом и НЕ растит
        // окно, поэтому у последней управляемой строки перед «+ добавить язык» меню из 3
        // пунктов упёрлось бы в нижний край HWND и обрезалось. Если под кебабом не помещается —
        // разворачиваем вверх (flip-up).
        double below = bottomRight.Y + RowMenuGap;
        MenuOffset.Y = Math.Round(below + RowMenu.ActualHeight <= RowMenuLayer.ActualHeight
            ? below
            : topLeft.Y - RowMenu.ActualHeight - RowMenuGap);

        PlayMenuPopIn();
    }

    /// <summary>
    /// Состав пунктов по состоянию строки вместе с доменным действием каждого.
    /// Управляемые строки (закреплённая и обычные) заканчиваются деструктивным «Убрать из
    /// набора»; системная статична — единственный путь «Сделать обычным».
    ///
    /// Прямого перехода «закреплённый → системный» нет: сперва «Открепить». Асимметрия
    /// намеренная, не пропуск.
    /// </summary>
    /// <summary>Японский Microsoft IME (0x0411) — единственный, у кого есть тогл форса хираганы.</summary>
    private const ushort JapaneseLangId = 0x0411;

    private IReadOnlyList<PanelMenuItemView> BuildMenuItems(PanelRowView row)
    {
        var lang = row.Profile;
        PanelMenuItemView[] baseItems = row.Kind switch
        {
            PanelRowKind.Pinned =>
            [
                new(Loc.Get("menu.unpin"), Unpin),
                new(Loc.Get("menu.removeFromSet"), () => RemoveFromSet(lang), IsDestructive: true),
            ],
            PanelRowKind.Normal =>
            [
                new(Loc.Get("menu.pin"), () => Pin(lang)),
                new(Loc.Get("menu.makeSystem"), () => MakePermanent(lang)),
                new(Loc.Get("menu.removeFromSet"), () => RemoveFromSet(lang), IsDestructive: true),
            ],
            _ => [new(Loc.Get("menu.makeNormal"), () => MakeManaged(lang))],
        };

        // Флагманский тогл «Форсировать хирагану» — только на управляемой японской строке,
        // перед деструктивным «Убрать из набора» (сохраняет разделитель-снизу).
        if (lang.LangId != JapaneseLangId || row.Kind is not (PanelRowKind.Pinned or PanelRowKind.Normal))
            return baseItems;

        var items = new List<PanelMenuItemView>(baseItems);
        int insertAt = items.FindIndex(i => i.IsDestructive);
        items.Insert(insertAt < 0 ? items.Count : insertAt, new PanelMenuItemView(
            Loc.Get("menu.forceHiragana"),
            ToggleForceHiragana,
            IsChecked: _settings.ForceHiraganaOnJapanese));
        return items;
    }

    /// <summary>
    /// Тогл флагманского форса хираганы (пункт «…»-меню японской строки). Персистим и будим
    /// хост: <see cref="_onDomainChanged"/> заставит MainWindow пере-оценить наблюдатель форса.
    /// </summary>
    private void ToggleForceHiragana()
    {
        _settings.ForceHiraganaOnJapanese = !_settings.ForceHiraganaOnJapanese;
        SettingsService.Save(_settings);
        _onDomainChanged();
    }

    /// <summary>Клик мимо меню внутри карты закрывает только меню — панель остаётся.</summary>
    private void OnRowMenuCatcher(object sender, MouseButtonEventArgs e)
    {
        CloseRowMenu();
        // Иначе клик всплыл бы до окна, где мимо-карты трактуется как закрытие панели.
        e.Handled = true;
    }

    /// <summary>Клик по пункту «…»-меню строки: исполнить действие и закрыть меню.</summary>
    private void OnRowMenuItem(object sender, RoutedEventArgs e)
    {
        var item = (PanelMenuItemView)((FrameworkElement)sender).DataContext;
        CloseRowMenu();
        item.Invoke();
    }

    private void CloseRowMenu()
    {
        _menuRow = null;
        RowMenuLayer.Visibility = Visibility.Collapsed;
        RowMenuItems.ItemsSource = null;
    }

    /// <summary>Тот же kj-pop, что и у карты, но для поповера меню.</summary>
    private void PlayMenuPopIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(120);

        RowMenu.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        MenuPopScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.98, 1, duration) { EasingFunction = ease });
        MenuPopScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.98, 1, duration) { EasingFunction = ease });
        MenuPopTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(-4, 0, duration) { EasingFunction = ease });
    }

    // --- Экран «+ добавить язык»: каталог замещает тело карты, gated-семантика ---

    /// <summary>
    /// Открыть экран-каталог: собрать доступные языки в двухстрочные строки и
    /// заместить тело карты (<see cref="MainBody"/> → <see cref="CatalogBody"/>). «…»-меню
    /// и секция настроек живут в MainBody, который прячется, — взаимное исключение выходит
    /// само собой.
    /// </summary>
    private void OnAddLanguage(object sender, RoutedEventArgs e)
    {
        CloseRowMenu();
        CloseSettings();

        var items = GetAvailableToAdd()
            .Select(lang =>
            {
                var (native, label, tooltip) = LanguageNames.Resolve(lang);
                return new CatalogEntryView
                {
                    Profile = lang,
                    Native = native,
                    Label = label,
                    Tooltip = tooltip,
                    // Аффорданс и действие клика ветвятся по регистрации пакета.
                    Installed = _service.IsRegisteredOnMachine(lang),
                };
            })
            .ToList();

        CatalogItems.ItemsSource = items;
        CatalogEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        MainBody.Visibility = Visibility.Collapsed;
        CatalogBody.Visibility = Visibility.Visible;

        // Каталог выше тела карты: досчитываем layout синхронно и пере-якориваем по
        // финальной высоте, чтобы при таскбаре снизу нижний край не ушёл под панель задач
        // (SizeChanged мог не покрыть последний прирост высоты).
        UpdateLayout();
        Reposition();
    }

    /// <summary>Вернуться с экрана-каталога к телу карты (стрелка «←» или Esc).</summary>
    private void OnCatalogBack(object sender, RoutedEventArgs e) => ShowMain();

    private void ShowMain()
    {
        CatalogBody.Visibility = Visibility.Collapsed;
        CatalogItems.ItemsSource = null;
        MainBody.Visibility = Visibility.Visible;

        UpdateLayout();
        Reposition();
    }

    /// <summary>
    /// Клик по строке каталога (gated-контракт): установленный язык кладётся в набор и
    /// экран закрывается (<see cref="AddToSet"/> → <see cref="Commit"/> пересобирает MainBody);
    /// у неустановленного пакета нет — ведём в параметры Windows (deep-link) и в набор
    /// НЕ кладём (реальную install-строку узнаём из реестра лишь после установки, дедуп по LangId).
    /// </summary>
    private void OnCatalogRow(object sender, RoutedEventArgs e)
    {
        var entry = (CatalogEntryView)((FrameworkElement)sender).DataContext;
        if (entry.Installed)
        {
            AddToSet(entry.Profile);
            ShowMain();
        }
        else
        {
            // Пакета нет → ведём в параметры Windows докачать его с инструкцией и
            // названием языка в буфере: пикер нельзя открыть/предзаполнить deep-link'ом.
            GuideToLanguageSettings(entry.Profile);
        }
    }

    /// <summary>Язык, к чьей установке ведёт открытый confirm-оверлей; <c>null</c> — оверлей закрыт.</summary>
    private LanguageProfile? _pendingDownload;

    /// <summary>
    /// Показать themed-оверлей с инструкцией к установке языка: пикер «Выбор языка для
    /// установки» нельзя открыть или предзаполнить deep-link'ом (проверено на Win11 — ms-settings
    /// ведёт лишь на страницу), поэтому объясняем шаги и по подтверждению кладём чистое имя языка
    /// в буфер, чтобы вставить в поиск пикера (Ctrl+V). Оверлей — слой в ЭТОМ же HWND (как «…»-меню),
    /// не отдельное окно: не роняет активацию и рисуется в теме панели, а не нативным MessageBox.
    /// </summary>
    private void GuideToLanguageSettings(LanguageProfile lang)
    {
        CloseRowMenu();
        _pendingDownload = lang;

        ConfirmText.Text = Loc.Get("confirm.body", LanguageNames.SearchTerm(lang));

        ConfirmLayer.Visibility = Visibility.Visible;
    }

    /// <summary>Подтверждение: кладём имя в буфер, открываем параметры и уходим (закрываем панель).</summary>
    private void OnConfirmOk(object sender, RoutedEventArgs e)
    {
        var lang = _pendingDownload;
        CloseConfirm();
        if (lang is null)
            return;

        try { System.Windows.Clipboard.SetText(LanguageNames.SearchTerm(lang)); }
        catch { /* буфер занят другим приложением — не критично, поиск можно набрать руками */ }

        OpenLanguageSettings();
        Dismiss();
    }

    private void OnConfirmCancel(object sender, RoutedEventArgs e) => CloseConfirm();

    /// <summary>Клик мимо карточки диалога закрывает только оверлей — панель остаётся.</summary>
    private void OnConfirmCatcher(object sender, MouseButtonEventArgs e)
    {
        CloseConfirm();
        e.Handled = true;
    }

    private void CloseConfirm()
    {
        ConfirmLayer.Visibility = Visibility.Collapsed;
        _pendingDownload = null;
    }

    /// <summary>
    /// Языки для каталога «+ добавить»: (установленный каталог ∪ курируемая библиотека)
    /// − набор − постоянные. Дедуп по <b>LangId</b>, а не по install-строке:
    /// установленный язык не должен всплыть второй раз как «скачать» из-за расхождения строк;
    /// установленные идут первыми, поэтому у совпавшего LangId побеждает реальная запись.
    /// </summary>
    private IReadOnlyList<LanguageProfile> GetAvailableToAdd()
    {
        var seen = new HashSet<ushort>();
        var result = new List<LanguageProfile>();
        foreach (var lang in _catalog.GetInstalledLanguages().Concat(LanguagePresets.Catalog))
        {
            if (!seen.Add(lang.LangId))
                continue;
            if (_settings.Contains(lang) || _settings.IsPermanent(lang))
                continue;
            result.Add(lang);
        }
        return result;
    }

    // --- Настройки: модальный оверлей ---

    /// <summary>
    /// Шестерёнка открывает/закрывает модальный оверлей настроек: слой в ЭТОМ же HWND
    /// (механика ConfirmLayer), не инлайн-аккордеон. Пока
    /// оверлей открыт, его catcher накрывает панель — клик по кебабу/«+ добавить» недоступен,
    /// так что взаимное исключение с «…»-меню и каталогом выходит по построению.
    /// </summary>
    private void OnToggleSettings(object sender, RoutedEventArgs e)
    {
        if (SettingsLayer.Visibility == Visibility.Visible)
        {
            CloseSettings();
            return;
        }

        CloseRowMenu();
        // Актуализируем чекбокс — автозапуск мог измениться извне между открытиями.
        LaunchOnStart.IsChecked = AutostartService.IsEnabled();
        SettingsLayer.Visibility = Visibility.Visible;
    }

    private void CloseSettings() => SettingsLayer.Visibility = Visibility.Collapsed;

    /// <summary>Клик мимо карточки настроек закрывает только оверлей — панель остаётся (как ConfirmLayer).</summary>
    private void OnSettingsCatcher(object sender, MouseButtonEventArgs e)
    {
        CloseSettings();
        e.Handled = true;
    }

    /// <summary>Чекбокс автозапуска: включает/выключает запись автозапуска, откатывая вид при ошибке.</summary>
    private void OnAutostartClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (LaunchOnStart.IsChecked == true)
                AutostartService.Enable();
            else
                AutostartService.Disable();
        }
        catch (Exception ex)
        {
            LaunchOnStart.IsChecked = AutostartService.IsEnabled();
            System.Windows.MessageBox.Show(
                Loc.Get("dialog.autostartFailed", ex.Message),
                "Keyji", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void OnExit(object sender, RoutedEventArgs e) => _onExit();

    // --- Тема: сегмент-тристейт ---

    /// <summary>Гасит персист при программной установке начального сегмента (иначе каждое открытие панели писало бы файл).</summary>
    private bool _suppressThemePersist;

    /// <summary>Выставить начальный сегмент по текущему <see cref="PanelTheme.Preference"/>, не триггеря персист.</summary>
    private void InitThemeSegment()
    {
        _suppressThemePersist = true;
        var seg = PanelTheme.Preference switch
        {
            ThemePreference.Light => ThemeLightSeg,
            ThemePreference.Dark => ThemeDarkSeg,
            _ => ThemeSystemSeg,
        };
        seg.IsChecked = true;
        _suppressThemePersist = false;
    }

    /// <summary>
    /// Выбор сегмента темы: применить эффективную тему сразу (live-swap через
    /// <see cref="PanelTheme.SetPreference"/>) и записать выбор в settings.json. При
    /// <c>System</c> панель снова следует ОС; <c>Light</c>/<c>Dark</c> фиксируют её.
    /// </summary>
    private void OnThemePick(object sender, RoutedEventArgs e)
    {
        if (_suppressThemePersist)
            return;

        var pref = Enum.Parse<ThemePreference>((string)((System.Windows.Controls.RadioButton)sender).Tag);
        PanelTheme.SetPreference(pref);
        _settings.Theme = pref;
        SettingsService.Save(_settings);
    }

    // --- Язык: дропдаун ---

    /// <summary>Гасит персист при программной установке начального пункта (иначе каждое открытие панели писало бы файл).</summary>
    private bool _suppressLanguagePersist;

    /// <summary>Выбрать начальный пункт дропдауна по сохранённому <see cref="KeyjiSettings.Language"/>, не триггеря персист.</summary>
    private void InitLanguageSelect()
    {
        _suppressLanguagePersist = true;
        foreach (System.Windows.Controls.ComboBoxItem item in LanguageSelect.Items)
        {
            if ((string)item.Tag == _settings.Language)
            {
                LanguageSelect.SelectedItem = item;
                break;
            }
        }
        // Незнакомое значение в файле → откат на «Системный» (первый пункт).
        if (LanguageSelect.SelectedItem is null)
            LanguageSelect.SelectedIndex = 0;
        _suppressLanguagePersist = false;
    }

    /// <summary>
    /// Выбор языка UI: применить культуру сразу (live через <see cref="Loc.ApplyPreference"/>),
    /// записать выбор в settings.json и перечитать строки, собираемые в code-behind —
    /// <see cref="BuildRows"/> (панель) и трей-тултип через <see cref="_onDomainChanged"/>.
    /// XAML-биндинги <c>{loc:T}</c> обновляются сами через INPC провайдера.
    /// </summary>
    private void OnLanguagePick(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguagePersist || LanguageSelect.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
            return;

        var pref = (string)item.Tag;
        Loc.Instance.ApplyPreference(pref);
        _settings.Language = pref;
        SettingsService.Save(_settings);

        BuildRows();
        _onDomainChanged();
    }

    // --- Утилиты ---

    private static bool KeyEquals(LanguageProfile lang, string installKey) =>
        string.Equals(lang.ToInstallString(), installKey, StringComparison.OrdinalIgnoreCase);

    private static void OpenLanguageSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "ms-settings:regionlanguage") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                Loc.Get("dialog.openSettingsFailed", ex.Message),
                "Keyji", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    // --- Анимации панели ---

    /// <summary>kj-pop: 0.12s, opacity 0→1 + translateY(-4px) scale(0.98) → none.</summary>
    private void PlayPopIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(120);

        Card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        PopScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.98, 1, duration) { EasingFunction = ease });
        PopScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.98, 1, duration) { EasingFunction = ease });
        PopTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(-4, 0, duration) { EasingFunction = ease });
    }

    /// <summary>kj-blink: курсор после вордмарка, шаговая анимация 1s.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var blink = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500))));
        blink.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1000))));
        Caret.BeginAnimation(OpacityProperty, blink);
    }

    // --- Dismiss ---

    private void OnDeactivated(object? sender, EventArgs e) => Dismiss();

    /// <summary>
    /// Единственный путь закрытия панели. Идемпотентен: <see cref="Window.Close"/> сам
    /// снимает активацию, из-за чего <c>Deactivated</c> прилетает ещё раз уже во время
    /// закрытия — повторный <c>Close</c> в этот момент WPF считает ошибкой и роняет
    /// процесс (<c>InvalidOperationException</c> из <c>VerifyNotClosing</c>). Тот же
    /// повтор ловит любой путь «сначала закрыли сами, потом потеряли фокус».
    /// </summary>
    private void Dismiss()
    {
        if (_closing)
            return;

        _closing = true;
        Close();
    }

    /// <summary>
    /// Клик по прозрачному полю вокруг карты закрывает панель. Проверка по
    /// <see cref="UIElement.IsMouseOver"/>, а не по источнику события: клик внутри карты
    /// всплывает до окна тем же маршрутом и иначе закрывал бы панель.
    /// </summary>
    private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Пока открыт любой оверлей (меню строки, confirm, настройки), клик-мимо
        // ловит его собственный catcher, а закрытие идёт через CloseRowMenu/CloseConfirm/
        // CloseSettings. Клик же по КАРТОЧКЕ оверлея всплывает сюда с IsMouseOver=false (мышь
        // над оверлеем, не над главной Card) — без этого гарда окно приняло бы его за «мимо
        // панели» и закрыло бы её целиком (клики по кнопкам гасятся, по пустому месту — нет).
        if (RowMenuLayer.Visibility == Visibility.Visible
            || ConfirmLayer.Visibility == Visibility.Visible
            || SettingsLayer.Visibility == Visibility.Visible)
            return;

        if (!Card.IsMouseOver)
            Dismiss();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Esc отступает по слоям: сначала «…»-меню, затем модальные оверлеи
        // (confirm, настройки), затем экран-каталог — назад к телу карты,
        // и лишь потом закрывает панель.
        if (e.Key == Key.Escape)
        {
            if (_menuRow is not null)
                CloseRowMenu();
            else if (ConfirmLayer.Visibility == Visibility.Visible)
                CloseConfirm();
            else if (SettingsLayer.Visibility == Visibility.Visible)
                CloseSettings();
            else if (CatalogBody.Visibility == Visibility.Visible)
                ShowMain();
            else
                Dismiss();

            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}
