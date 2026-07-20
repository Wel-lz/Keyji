using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Keyji.Layouts;
using Keyji.Localization;
using Keyji.Settings;
using Keyji.Startup;
using Keyji.Theming;
using Wpf.Ui.Controls;

namespace Keyji;

/// <summary>
/// Хост трей-иконки + окно первого запуска. Основной UI — кастомная панель-карта
/// (<see cref="Panel.PanelWindow"/>), открываемая ПКМ по трей-иконке (нативное меню удалено).
/// Иконка трея = статус закреплённого; левый клик тоглит закреплённый, а при
/// его отсутствии открывает панель (три состояния трея). Окно вырождается в
/// приветствие первого запуска: держит лишь <c>InfoBar</c> и пикер постоянных
/// языков; автозапуск и список активных раскладок переехали в панель. Закрытие
/// окна прячет его в трей, реальный выход — из футера панели.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly ILayoutService _service = new LayoutService();
    private readonly IInstalledCatalog _catalog = new InstalledCatalog();
    private readonly Input.ForegroundTracker _foreground = new();
    private readonly KeyjiSettings _settings;

    /// <summary>Закреплённый язык из набора или <c>null</c> — закреп опционален.</summary>
    private LanguageProfile? _pinned;
    private bool _exiting;

    // Отложенный запрос переключения на закреплённый язык (return-time): ставится при
    // включении из трея, исполняется когда фокус вернётся на пользовательское окно.
    private LanguageProfile? _pendingSwitch;

    // Опрос форса хираганы: ловит смену языка «на месте» (Shift+Tab/Win+Space) в текущем
    // редакторе — её WinEvent не отдаёт. Форсим только на rising-edge «стал японским».
    private readonly System.Windows.Threading.DispatcherTimer _hiraganaPoll;
    private IntPtr _pollHwnd;
    private bool _pollWasJp;

    // Открытая панель (или null). Держим ссылку ради тоггла по повторному ПКМ.
    private Panel.PanelWindow? _panel;

    // Момент самозакрытия панели: клик по трею при открытой панели уводит фокус на shell,
    // панель закрывается по Deactivated ДО того, как долетит трей-событие. Гасим
    // переоткрытие на короткое окно после закрытия, чтобы повторный клик читался как тоггл.
    private DateTime _panelClosedAt = DateTime.MinValue;

    // Иконки трея растеризуем один раз: H.NotifyIcon принимает готовую GDI-иконку
    // (System.Drawing.Icon), а логотип у нас векторный (DrawingImage).
    private System.Drawing.Icon? _iconOn;
    private System.Drawing.Icon? _iconOff;

    public MainWindow(bool firstRun = false)
    {
        InitializeComponent();

        _settings = SettingsService.Load();

        // Тема: накатываем сохранённый выбор поверх системного baseline из
        // PanelTheme.Initialize. Здесь — до любого Show (все пути показа окна идут в
        // App.OnStartup ПОСЛЕ конструктора), поэтому вспышки чужой темы нет.
        PanelTheme.SetPreference(_settings.Theme);

        // Язык UI: так же накатываем сохранённый выбор поверх baseline из Loc.Initialize
        // (культура ОС). До Show → без вспышки чужого языка на первом кадре.
        Loc.Instance.ApplyPreference(_settings.Language);

        // Первый запуск: сид постоянных языков = каталог − управляемые. Это базовые
        // раскладки, стоявшие до Keyji (English/русский), которые Keyji не должен
        // выключать. Флагманский японский исключается автоматически — он
        // управляемый по пресету, поэтому в разность не попадает (carve-out: иначе
        // на машинах с предустановленным JP TIP фича переключения умерла бы).
        if (firstRun)
            SeedPermanentLanguages();

        _pinned = ResolvePinned(_settings);
        // Нормализуем и создаём файл на первом запуске (пишем актуальный ключ; null —
        // штатное состояние «нет закрепа»).
        _settings.PinnedKey = _pinned?.ToInstallString();
        SettingsService.Save(_settings);

        _foreground.UserWindowForeground += OnUserWindowForeground;

        _hiraganaPoll = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _hiraganaPoll.Tick += OnHiraganaPoll;
        UpdateHiraganaWatch();

        RefreshState();
    }

    /// <summary>
    /// Фокус вернулся на пользовательское окно. Если ждёт отложенное переключение (тогл
    /// закреплённого из трея) — исполняем его здесь: окно уже на переднем плане, значит
    /// WM_INPUTLANGCHANGEREQUEST реально применится (в фоне затирался). Цель — hwnd самого
    /// события (не LastUserWindow: её этот же ивент только что перезаписал — гонка).
    /// Флаг гасим ДО SwitchTo, чтобы повторный фокус-ивент не запустил переключение дважды.
    /// </summary>
    private void OnUserWindowForeground(IntPtr hwnd)
    {
        // Базлайн опроса форса: фокус вошёл в это окно — оно владелец focus-in, поллер на
        // нём повторно НЕ стреляет (edge Shift+Tab «на месте» ловит уже он).
        _pollHwnd = hwnd;
        _pollWasJp = _service.IsJapaneseActive(hwnd);

        var pending = _pendingSwitch;
        if (pending is not null)
        {
            // Путь тогла из трея: активировать закреплённый и (для JP) форсить хирагану.
            _pendingSwitch = null;
            _service.SwitchTo(pending, hwnd);
            _pollWasJp = _service.IsJapaneseActive(hwnd); // после активации — не давать поллеру повторить
            Dispatcher.BeginInvoke(RefreshState);
            return;
        }

        // Фокус вошёл в JP-активное окно (переключение приложений / возврат фокуса): форсим
        // хирагану, если фича включена. Взаимоисключающе с веткой pending — иначе двойной форс.
        if (_settings.ForceHiraganaOnJapanese && _pollWasJp)
            _service.ForceJapaneseMode(hwnd);
    }

    /// <summary>
    /// Опрос форса хираганы (~250 мс): ловит смену языка «на месте» (Shift+Tab/Win+Space) в
    /// текущем редакторе, которую WinEvent'ы не отдают. Форсим ТОЛЬКО на rising-edge «стал
    /// японским» — не на каждом тике, иначе затирали бы намеренную катакану/полуширину внутри JP.
    /// </summary>
    private void OnHiraganaPoll(object? sender, EventArgs e)
    {
        var hwnd = _foreground.LastUserWindow;
        if (hwnd == IntPtr.Zero)
            return;

        bool isJp = _service.IsJapaneseActive(hwnd);
        if (hwnd != _pollHwnd)
        {
            // Смена окна: focus-in владеет WinEvent — здесь только обновляем базлайн, без форса.
            _pollHwnd = hwnd;
            _pollWasJp = isJp;
            return;
        }

        if (isJp && !_pollWasJp && _settings.ForceHiraganaOnJapanese)
            _service.ForceJapaneseMode(hwnd);
        _pollWasJp = isJp;
    }

    /// <summary>
    /// Запускает/останавливает опрос форса хираганы: работает, только пока фича включена И
    /// японский есть в управляемом наборе (иначе форсить нечего). Зовётся на старте и когда
    /// панель меняет настройку/набор (<see cref="OnPanelChanged"/>).
    /// </summary>
    private void UpdateHiraganaWatch()
    {
        bool want = _settings.ForceHiraganaOnJapanese
            && _settings.ManagedLanguages.Any(l => l.LangId == 0x0411);
        if (want)
        {
            if (!_hiraganaPoll.IsEnabled)
                _hiraganaPoll.Start();
        }
        else
        {
            _hiraganaPoll.Stop();
        }
    }

    /// <summary>
    /// Пометить все установленные (но не управляемые) языки постоянными — безопасный
    /// дефолт первого запуска: ничего из уже стоявшего Keyji не тронет, пока пользователь
    /// явно не снимет галочку в пикере онбординга или не пометит язык из панели.
    /// </summary>
    private void SeedPermanentLanguages()
    {
        foreach (var lang in _catalog.GetInstalledLanguages())
        {
            if (_settings.Contains(lang) || _settings.IsPermanent(lang))
                continue;
            _settings.PermanentLanguages.Add(lang);
        }
    }

    /// <summary>
    /// Закреплённый язык из набора по ключу, либо <c>null</c>: закреп опционален,
    /// авто-избрания нового закрепа больше нет. Ключ, не совпавший ни с одним
    /// управляемым языком (в т.ч. <c>null</c>), даёт <c>null</c> — «нет закрепа».
    /// </summary>
    private static LanguageProfile? ResolvePinned(KeyjiSettings settings) =>
        settings.ManagedLanguages.FirstOrDefault(l => l.ToInstallString() == settings.PinnedKey);

    /// <summary>
    /// Синхронизировать иконку/тултип трея с фактическим состоянием (три состояния):
    /// нет закрепа → Off + «язык не закреплён»; закреплённый вкл → On; закреплённый выкл → Off.
    /// </summary>
    private void RefreshState()
    {
        if (_pinned is null)
        {
            TrayIcon.Icon = IconOff;
            TrayIcon.ToolTipText = Loc.Get("tray.tooltip.unpinned");
            return;
        }

        bool enabled = _service.IsEnabled(_pinned);
        TrayIcon.Icon = enabled ? IconOn : IconOff;
        TrayIcon.ToolTipText = Loc.Get(
            "tray.tooltip.pinned",
            _pinned.DisplayName,
            Loc.Get(enabled ? "tray.status.enabled" : "tray.status.disabled"));
    }

    private System.Drawing.Icon IconOn => _iconOn ??= Rasterize("TrayIconOn");
    private System.Drawing.Icon IconOff => _iconOff ??= Rasterize("TrayIconOff");

    /// <summary>Растеризовать векторный DrawingImage из ресурсов в GDI-иконку для трея.</summary>
    private static System.Drawing.Icon Rasterize(string resourceKey, int size = 256)
    {
        var drawingImage = (DrawingImage)Application.Current.Resources[resourceKey];
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawImage(drawingImage, new Rect(0, 0, size, size));

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;

        using var bitmap = new System.Drawing.Bitmap(stream);
        // GetHicon отдаёт unmanaged-хэндл; для двух долгоживущих иконок утечка не значима.
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    // --- Трей: панель (основной UI) ---

    /// <summary>ПКМ по трею открывает/закрывает панель (замена нативного меню).</summary>
    private void TrayIcon_OnRightMouseDown(object sender, RoutedEventArgs e) => TogglePanel();

    /// <summary>
    /// Левый клик по трею: тоглит закреплённый; при отсутствии закрепа тоглить нечего —
    /// открываем панель (единое правило, пустой набор — частный случай «нет закрепа»).
    /// </summary>
    private void TrayIcon_OnLeftMouseUp(object sender, RoutedEventArgs e)
    {
        if (_pinned is null)
            TogglePanel();
        else
            ToggleFromCurrent();
    }

    private void TogglePanel()
    {
        if (_panel is not null)
        {
            _panel.Activate();
            return;
        }

        // Свежее самозакрытие тем же кликом — это тоггл «закрыть», не переоткрывать.
        if ((DateTime.UtcNow - _panelClosedAt).TotalMilliseconds < 300)
            return;

        ShowPanel();
    }

    /// <summary>Открыть живую панель поверх трея (основной UI, вход по ПКМ трея).</summary>
    public void ShowPanel()
    {
        if (_panel is not null)
        {
            _panel.Activate();
            return;
        }

        var panel = new Panel.PanelWindow(_service, _catalog, _settings, OnPanelChanged, RequestExit);
        panel.Closed += OnPanelClosed;
        _panel = panel;
        panel.ShowAtTray();
    }

    private void OnPanelClosed(object? sender, EventArgs e)
    {
        if (sender is Panel.PanelWindow p)
            p.Closed -= OnPanelClosed;
        _panel = null;
        _panelClosedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Панель изменила набор (пин/remove/permanent/add/enable): она мутирует тот же
    /// <see cref="_settings"/> и уже персистит его сама — здесь лишь пере-разрешаем
    /// закреплённый и освежаем трей.
    /// </summary>
    private void OnPanelChanged()
    {
        _pinned = ResolvePinned(_settings);
        RefreshState();
        // Панель могла переключить форс хираганы или состав набора — пере-оцениваем опрос.
        UpdateHiraganaWatch();
    }

    // --- Трей: левый клик по закреплённому ---

    private void ToggleFromCurrent()
    {
        if (_pinned is null)
            return;

        // Закреплённый без бинарей на машине нельзя включить — левый клик ведёт в
        // Settings докачать (симметрия с пунктом «⤓ Скачать» в панели).
        if (!_service.IsRegisteredOnMachine(_pinned))
        {
            OpenLanguageSettings();
            return;
        }

        bool enable = !_service.IsEnabled(_pinned);
        ApplyToggle(_pinned, enable);

        // Включили закреплённый → «хочу печатать на нём сейчас». Но сейчас редактор НЕ на
        // переднем плане (клик увёл фокус на shell), а переключение в фон затирается при
        // возврате фокуса. Поэтому не переключаем здесь, а ставим отложенный запрос — он
        // выполнится в OnUserWindowForeground, когда фокус вернётся на редактор (return-time).
        _pendingSwitch = (enable && _service.IsEnabled(_pinned)) ? _pinned : null;
    }

    /// <summary>Применить желаемое состояние языка (enable/disable) с откатом при ошибке.</summary>
    private void ApplyToggle(LanguageProfile lang, bool enable)
    {
        bool ok = enable
            ? _service.EnableLanguage(lang)
            : _service.DisableLanguage(lang);

        if (!ok)
        {
            RefreshState();
            System.Windows.MessageBox.Show(
                Loc.Get("dialog.toggleFailed", Loc.Get(enable ? "verb.enable" : "verb.disable"), lang.DisplayName),
                "Keyji", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        RefreshState();
    }

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

    // --- Запуск / автозапуск / окно первого запуска ---

    /// <summary>
    /// Старт при автозапуске: поднять трей-иконку, не показывая окно.
    /// H.NotifyIcon регистрирует иконку только когда окно реально показано и
    /// отрисовано (создание HICON асинхронно). Поэтому показываем окно за
    /// пределами экрана и прячем сразу после первого рендера — иконка уже
    /// зарегистрирована, окно нигде не мелькает.
    /// </summary>
    public void StartInTray()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        ShowActivated = false;
        ShowInTaskbar = false;
        ContentRendered += HideAfterInitialRender;
        Show();
    }

    private void HideAfterInitialRender(object? sender, EventArgs e)
    {
        ContentRendered -= HideAfterInitialRender;
        Hide();
    }

    /// <summary>
    /// Онбординг первого запуска: показываем окно с приветственным баннером
    /// один раз. В отличие от <see cref="StartInTray"/> окно показывается штатно
    /// (по центру, активным) — этот же показ регистрирует трей-иконку у H.NotifyIcon,
    /// поэтому отдельный off-screen-трюк с <see cref="HideAfterInitialRender"/> не нужен
    /// и гонки «показали → тут же спрятали» не возникает. Закрытие окна прячет его
    /// в трей (обычное <see cref="OnClosing"/>), дальше приложение живёт панелью-first.
    /// </summary>
    public void ShowFirstRunWelcome()
    {
        WelcomeRoot.Visibility = Visibility.Visible;
        BuildPermanentPicker();
        ShowWindow();
    }

    /// <summary>
    /// Наполнить пикер постоянных языков чекбоксами из <see cref="KeyjiSettings.PermanentLanguages"/>
    /// (все отмечены). Снятие галочки убирает язык из постоянных сразу (DropPermanent + Save) —
    /// он становится доступен в «Добавить язык». Карточку показываем, только если есть что выбирать.
    /// </summary>
    private void BuildPermanentPicker()
    {
        PermanentPickerPanel.Children.Clear();

        // Снимок: DropPermanent мутирует список, по нему нельзя итерироваться в обработчике.
        foreach (var lang in _settings.PermanentLanguages.ToList())
        {
            var box = new System.Windows.Controls.CheckBox
            {
                Content = lang.DisplayName,
                IsChecked = true,
                Tag = lang,
                Margin = new Thickness(0, 2, 0, 2),
            };
            box.Unchecked += PermanentPick_OnUnchecked;
            box.Checked += PermanentPick_OnChecked;
            PermanentPickerPanel.Children.Add(box);
        }

        PermanentPickerCard.Visibility = PermanentPickerPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void PermanentPick_OnUnchecked(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox { Tag: LanguageProfile lang } &&
            _settings.DropPermanent(lang))
            SettingsService.Save(_settings);
    }

    private void PermanentPick_OnChecked(object sender, RoutedEventArgs e)
    {
        // Повторная отметка (передумал): вернуть язык в постоянные. AddToSet-семантики нет —
        // прямое добавление в список, дедуп по IsPermanent.
        if (sender is System.Windows.Controls.CheckBox { Tag: LanguageProfile lang } &&
            !_settings.IsPermanent(lang))
        {
            _settings.PermanentLanguages.Add(lang);
            SettingsService.Save(_settings);
        }
    }

    private void ShowWindow()
    {
        RefreshState();
        ShowInTaskbar = true;
        if (Left <= -10000)
            CenterOnScreen();
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void CenterOnScreen()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    private void RequestExit()
    {
        _exiting = true;
        _hiraganaPoll.Stop();
        _foreground.Dispose();
        TrayIcon.Dispose();
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
            // Приветствие первого запуска одноразовое: гасим его при закрытии окна,
            // иначе оно всплыло бы снова при следующем показе — окно-то одно
            // и то же долгоживущее. На обычном пути контент уже свёрнут.
            WelcomeRoot.Visibility = Visibility.Collapsed;
        }

        base.OnClosing(e);
    }
}
