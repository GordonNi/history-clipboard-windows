using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using HistoryClipboard.App.Windows;
using HistoryClipboard.Core.Capture;
using HistoryClipboard.Core.History;
using HistoryClipboard.Infrastructure.Settings;

namespace HistoryClipboard.App;

public partial class MainWindow : Window, IDisposable
{
    private readonly ClipboardCaptureService capture;
    private readonly ClipboardCopyService copy;
    private readonly ClipboardHistory history;
    private readonly LaunchAtLoginController launchAtLogin;
    private readonly Action<GlobalHotkeyOption> saveHotkey;
    private readonly Action<RetentionPeriod> saveRetention;
    private bool allowClose;
    private bool capturePending;
    private bool isCapturing;
    private bool isCopying;
    private bool isInitializingLaunchAtLogin;
    private bool isInitializingRetention;
    private bool isInitializingHotkey;
    private bool ignoreNextDeactivation;
    private ClipboardFormatListener? listener;
    private GlobalHotkeyController? hotkey;
    private GlobalHotkeyOption hotkeyOption;
    private bool launchAtLoginEnabled;
    private string? launchAtLoginReadError;
    private RetentionPeriod retentionPeriod;
    private bool suppressAutoHide;

    public MainWindow(
        ClipboardHistory history,
        ClipboardCaptureService capture,
        ClipboardCopyService copy,
        RetentionPeriod retentionPeriod,
        Action<RetentionPeriod> saveRetention,
        GlobalHotkeyOption hotkeyOption,
        Action<GlobalHotkeyOption> saveHotkey,
        LaunchAtLoginController launchAtLogin)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(saveRetention);
        ArgumentNullException.ThrowIfNull(saveHotkey);
        ArgumentNullException.ThrowIfNull(launchAtLogin);

        this.history = history;
        this.capture = capture;
        this.copy = copy;
        this.retentionPeriod = retentionPeriod;
        this.saveRetention = saveRetention;
        this.hotkeyOption = hotkeyOption;
        this.saveHotkey = saveHotkey;
        this.launchAtLogin = launchAtLogin;
        InitializeComponent();
        InitializeRetentionOptions();
        InitializeHotkeyOptions();
        InitializeLaunchAtLogin();
        RefreshHistory();
        SourceInitialized += OnSourceInitialized;
        Deactivated += OnDeactivated;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    public void ToggleFromTray()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        ShowFromTray();
    }

    public void ShowFromTray()
    {
        var windowHandle = new WindowInteropHelper(this).EnsureHandle();
        var target = WindowPlacementController.SelectTarget(windowHandle);
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        var popupWidth = ActualWidth > 0 ? ActualWidth : Width;
        var popupHeight = ActualHeight > 0 ? ActualHeight : Height;
        WindowPlacementController.PlaceWindow(
            windowHandle,
            target,
            popupWidth,
            popupHeight,
            16);
        Activate();
    }

    public void PrepareForExit()
    {
        allowClose = true;
    }

    public void PrepareForTrayInteraction()
    {
        if (IsVisible && IsActive)
        {
            ignoreNextDeactivation = true;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        listener = new ClipboardFormatListener(windowHandle);
        listener.ClipboardChanged += OnClipboardChanged;
        hotkey = new GlobalHotkeyController(windowHandle, ToggleFromTray);
        UpdateHotkeyStatus(hotkey.SetHotkey(hotkeyOption));
        if (launchAtLoginReadError is not null)
        {
            StatusText.Text +=
                $" 登录后自动启动状态读取失败：{launchAtLoginReadError}；应用仍可正常使用。";
        }
    }

    private async void OnClipboardChanged(object? sender, EventArgs e)
    {
        if (copy.ShouldSuppressCurrentUpdate())
        {
            return;
        }

        if (isCapturing)
        {
            capturePending = true;
            return;
        }

        isCapturing = true;
        try
        {
            do
            {
                capturePending = false;
                var result = await capture.CaptureAsync();
                UpdateAfterCapture(result);
            }
            while (capturePending);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"捕获失败：{exception.GetType().Name}";
        }
        finally
        {
            isCapturing = false;
        }
    }

    private async void OnCopyItemClicked(object sender, RoutedEventArgs e)
    {
        if (isCopying || sender is not FrameworkElement
            {
                DataContext: ClipboardHistoryItem item,
            })
        {
            return;
        }

        isCopying = true;
        try
        {
            var result = await copy.CopyAsync(item);
            StatusText.Text = result switch
            {
                ClipboardCopyResult.Copied => "已复制到剪贴板。",
                ClipboardCopyResult.Unavailable => "剪贴板暂时被占用，复制未完成。",
                _ => throw new InvalidOperationException(
                    $"Unsupported clipboard copy result '{result}'."),
            };
        }
        catch (Exception exception)
        {
            StatusText.Text = $"复制失败：{exception.GetType().Name}";
        }
        finally
        {
            isCopying = false;
        }
    }

    private void OnTogglePinClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement
            {
                DataContext: ClipboardHistoryItem item,
            })
        {
            return;
        }

        var updated = history.TogglePin(item.Id);
        if (updated is null)
        {
            StatusText.Text = "这条历史记录已不存在。";
            return;
        }

        StatusText.Text = updated.IsPinned ? "已固定这条记录。" : "已取消固定。";
        RefreshHistory();
    }

    private void OnDeleteItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement
            {
                DataContext: ClipboardHistoryItem item,
            })
        {
            return;
        }

        var removed = history.Delete(item.Id);
        if (removed is null)
        {
            StatusText.Text = "这条历史记录已不存在。";
            return;
        }

        var itemType = removed.Type == ClipboardItemType.Text ? "文字" : "图片";
        StatusText.Text = $"已删除一条{itemType}记录。";
        RefreshHistory();
    }

    private void OnToggleCaptureClicked(object sender, RoutedEventArgs e)
    {
        var isPaused = capture.TogglePaused();
        PauseCaptureButton.IsChecked = isPaused;
        StatusText.Text = isPaused
            ? "已暂停捕获；现有历史仍可查看和复制。"
            : "已恢复捕获新的文字和图片。";
    }

    private void OnClearHistoryClicked(object sender, RoutedEventArgs e)
    {
        if (history.Items.Count == 0)
        {
            StatusText.Text = "历史记录已经为空。";
            return;
        }

        MessageBoxResult confirmation;
        suppressAutoHide = true;
        try
        {
            confirmation = System.Windows.MessageBox.Show(
                this,
                $"确认清空全部 {history.Items.Count} 条历史记录？固定记录和图片文件也会删除。",
                "清空全部历史",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
        }
        finally
        {
            suppressAutoHide = false;
            Activate();
        }

        if (confirmation != MessageBoxResult.Yes)
        {
            StatusText.Text = "已保留全部历史记录。";
            return;
        }

        var removed = history.Clear();
        RefreshHistory();
        StatusText.Text = $"已清空 {removed.Count} 条历史记录。";
    }

    private void OnRetentionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (isInitializingRetention
            || RetentionComboBox.SelectedItem is not RetentionOption selection)
        {
            return;
        }

        try
        {
            saveRetention(selection.Value);
        }
        catch (Exception exception)
        {
            SelectRetention(retentionPeriod);
            StatusText.Text = $"保留期限保存失败：{exception.GetType().Name}";
            return;
        }

        retentionPeriod = selection.Value;
        try
        {
            var removed = history.RemoveExpired(retentionPeriod);
            RefreshHistory();
            StatusText.Text = removed.Count == 0
                ? $"保留期限已设为 {selection.Label}，没有过期记录。"
                : $"保留期限已设为 {selection.Label}，已清理 {removed.Count} 条过期记录。";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"保留期限已保存，清理将在下次启动重试：{exception.GetType().Name}";
        }
    }

    private void OnHotkeyChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (isInitializingHotkey
            || HotkeyComboBox.SelectedItem is not HotkeyOption selection)
        {
            return;
        }

        try
        {
            saveHotkey(selection.Value);
        }
        catch (Exception exception)
        {
            SelectHotkey(hotkeyOption);
            StatusText.Text = $"全局快捷键保存失败：{exception.GetType().Name}";
            return;
        }

        hotkeyOption = selection.Value;
        var result = hotkey?.SetHotkey(hotkeyOption)
            ?? GlobalHotkeyRegistrationResult.Disabled;
        UpdateHotkeyStatus(result);
    }

    private void UpdateHotkeyStatus(GlobalHotkeyRegistrationResult result)
    {
        StatusText.Text = result switch
        {
            GlobalHotkeyRegistrationResult.Registered =>
                $"正在监听剪贴板；全局快捷键为 {FormatHotkey(hotkeyOption)}。",
            GlobalHotkeyRegistrationResult.Disabled =>
                "正在监听剪贴板；全局快捷键已禁用。",
            GlobalHotkeyRegistrationResult.Unavailable =>
                $"正在监听剪贴板；{FormatHotkey(hotkeyOption)} 已被其他应用占用，本次未启用快捷键。",
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
    }

    private void OnLaunchAtLoginChanged(object sender, RoutedEventArgs e)
    {
        if (isInitializingLaunchAtLogin)
        {
            return;
        }

        var requestedState = LaunchAtLoginCheckBox.IsChecked == true;
        var result = launchAtLogin.SetEnabled(requestedState);
        if (!result.Succeeded)
        {
            SelectLaunchAtLogin(launchAtLoginEnabled);
            StatusText.Text =
                $"登录后自动启动设置失败：{result.ErrorType}；应用仍可正常使用。";
            return;
        }

        launchAtLoginEnabled = result.IsEnabled;
        StatusText.Text = launchAtLoginEnabled
            ? "已启用登录后自动启动（仅当前 Windows 用户）。"
            : "已禁用登录后自动启动。";
    }

    private void UpdateAfterCapture(ClipboardCaptureResult result)
    {
        StatusText.Text = result switch
        {
            ClipboardCaptureResult.CapturedText => "已捕获新的文字。",
            ClipboardCaptureResult.CapturedImage => "已捕获新的图片。",
            ClipboardCaptureResult.NoSupportedContent => "本次剪贴板内容不是支持的文字或图片。",
            ClipboardCaptureResult.Duplicate => "已忽略连续重复文字。",
            ClipboardCaptureResult.Unavailable => "剪贴板暂时被占用，已在有限重试后跳过。",
            ClipboardCaptureResult.Paused => "捕获已暂停。",
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };

        if (result is ClipboardCaptureResult.CapturedText or ClipboardCaptureResult.CapturedImage)
        {
            RefreshHistory();
        }
    }

    private void RefreshHistory()
    {
        var result = history.Query(SearchTextBox.Text);
        HistoryList.ItemsSource = result.Items.ToArray();
        SearchResultText.Text = $"{result.Items.Count} 个结果";
        TextCountText.Text = $"{result.TotalTextCount} 条文字";
        ImageCountText.Text = $"{result.TotalImageCount} 张图片";

        var showEmptyState = result.Items.Count == 0;
        EmptyStatePanel.Visibility = showEmptyState ? Visibility.Visible : Visibility.Collapsed;
        if (showEmptyState)
        {
            var isSearching = !string.IsNullOrWhiteSpace(SearchTextBox.Text);
            EmptyStateBadgeText.Text = isSearching ? "搜索" : "Ctrl + C";
            EmptyStateTitleText.Text = isSearching ? "没有匹配结果" : "还没有历史记录";
            EmptyStateDescriptionText.Text = isSearching
                ? "换个关键词，或清空搜索框查看全部历史。"
                : "复制一段文字或截取图片后，会出现在这里。";
        }
    }

    private void InitializeRetentionOptions()
    {
        RetentionComboBox.ItemsSource = new[]
        {
            new RetentionOption(RetentionPeriod.OneDay, "1 天"),
            new RetentionOption(RetentionPeriod.ThreeDays, "3 天"),
            new RetentionOption(RetentionPeriod.FiveDays, "5 天"),
        };
        SelectRetention(retentionPeriod);
    }

    private void InitializeHotkeyOptions()
    {
        HotkeyComboBox.ItemsSource = new[]
        {
            new HotkeyOption(GlobalHotkeyOption.ControlShiftV, "Ctrl+Shift+V"),
            new HotkeyOption(GlobalHotkeyOption.ControlAltV, "Ctrl+Alt+V"),
            new HotkeyOption(GlobalHotkeyOption.ControlShiftH, "Ctrl+Shift+H"),
            new HotkeyOption(GlobalHotkeyOption.Disabled, "禁用"),
        };
        SelectHotkey(hotkeyOption);
    }

    private void InitializeLaunchAtLogin()
    {
        var result = launchAtLogin.GetStatus();
        launchAtLoginEnabled = result.Succeeded && result.IsEnabled;
        launchAtLoginReadError = result.Succeeded ? null : result.ErrorType;
        SelectLaunchAtLogin(launchAtLoginEnabled);
        LaunchAtLoginCheckBox.IsEnabled = result.Succeeded;
        if (!result.Succeeded)
        {
            LaunchAtLoginCheckBox.ToolTip =
                "无法读取当前用户的登录启动设置；重新启动应用后可重试。";
        }
    }

    private void SelectHotkey(GlobalHotkeyOption value)
    {
        isInitializingHotkey = true;
        try
        {
            HotkeyComboBox.SelectedValue = value;
        }
        finally
        {
            isInitializingHotkey = false;
        }
    }

    private static string FormatHotkey(GlobalHotkeyOption option)
    {
        return option switch
        {
            GlobalHotkeyOption.ControlShiftV => "Ctrl+Shift+V",
            GlobalHotkeyOption.ControlAltV => "Ctrl+Alt+V",
            GlobalHotkeyOption.ControlShiftH => "Ctrl+Shift+H",
            GlobalHotkeyOption.Disabled => "已禁用",
            _ => throw new ArgumentOutOfRangeException(nameof(option)),
        };
    }

    private void SelectLaunchAtLogin(bool value)
    {
        isInitializingLaunchAtLogin = true;
        try
        {
            LaunchAtLoginCheckBox.IsChecked = value;
        }
        finally
        {
            isInitializingLaunchAtLogin = false;
        }
    }

    private void SelectRetention(RetentionPeriod value)
    {
        isInitializingRetention = true;
        try
        {
            RetentionComboBox.SelectedValue = value;
        }
        finally
        {
            isInitializingRetention = false;
        }
    }

    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            RefreshHistory();
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
        {
            return;
        }

        WindowState = WindowState.Normal;
        Hide();
    }

    private async void OnDeactivated(object? sender, EventArgs e)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(120));
        if (ignoreNextDeactivation)
        {
            ignoreNextDeactivation = false;
            return;
        }

        if (!suppressAutoHide && !allowClose && IsVisible && !IsActive)
        {
            Hide();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Dispose();
    }

    public void Dispose()
    {
        hotkey?.Dispose();
        hotkey = null;
        if (listener is not null)
        {
            listener.ClipboardChanged -= OnClipboardChanged;
            listener.Dispose();
            listener = null;
        }

        GC.SuppressFinalize(this);
    }

    private sealed record RetentionOption(RetentionPeriod Value, string Label);

    private sealed record HotkeyOption(GlobalHotkeyOption Value, string Label);
}
