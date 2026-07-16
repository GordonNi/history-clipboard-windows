using System.IO;
using System.Windows;
using System.Windows.Interop;
using HistoryClipboard.App.Startup;
using HistoryClipboard.App.Windows;
using HistoryClipboard.Core.Capture;
using HistoryClipboard.Core.History;
using HistoryClipboard.Infrastructure.Persistence;
using HistoryClipboard.Infrastructure.Settings;

namespace HistoryClipboard.App;

public partial class App : System.Windows.Application, IDisposable
{
    private bool isExiting;
    private SingleInstanceController? singleInstance;
    private TaskbarRestartRecoveryController? taskbarRestartRecovery;
    private TrayIconController? trayIcon;
    private MainWindow? window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        singleInstance = SingleInstanceController.TryStart(ActivateExistingWindow);
        if (singleInstance is null)
        {
            Shutdown();
            return;
        }

        try
        {
            InitializePrimaryInstance();
        }
        catch (Exception exception)
        {
            HandleStartupFailure(exception);
        }
    }

    private void InitializePrimaryInstance()
    {

        var applicationDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HistoryClipboard");
        var databasePath = Path.Combine(applicationDirectory, "history.db");
        var settingsStore = new JsonHistorySettingsStore(
            Path.Combine(applicationDirectory, "settings.json"));
        var launchAtLogin = new LaunchAtLoginController(
            Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "HistoryClipboard.App.exe"));
        var retentionPeriod = settingsStore.LoadRetention();
        var hotkeyOption = settingsStore.LoadHotkey();
        var repository = new SqliteClipboardHistoryRepository(databasePath);
        var history = new ClipboardHistory(repository: repository);
        history.RemoveExpired(retentionPeriod);
        var capture = new ClipboardCaptureService(history, new WpfClipboardContentReader());
        var copy = new ClipboardCopyService(
            new WpfClipboardHistoryWriter(),
            new WindowsClipboardSequenceNumber());
        window = new MainWindow(
            history,
            capture,
            copy,
            retentionPeriod,
            settingsStore.SaveRetention,
            hotkeyOption,
            settingsStore.SaveHotkey,
            launchAtLogin);
        MainWindow = window;
        trayIcon = new TrayIconController(
            window.PrepareForTrayInteraction,
            window.ShowFromTray,
            window.ToggleFromTray,
            ExitApplication);
        window.ShowFromTray();
        taskbarRestartRecovery = new TaskbarRestartRecoveryController(
            new WindowInteropHelper(window).Handle,
            trayIcon.Republish);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        DisposeTaskbarRestartRecovery();
        DisposeTrayIcon();
        DisposeWindow();
        DisposeSingleInstance();
        GC.SuppressFinalize(this);
    }

    private void HandleStartupFailure(Exception exception)
    {
        StartupFailureCoordinator.Handle(
            exception,
            [
                DisposeTaskbarRestartRecovery,
                DisposeTrayIcon,
                DisposeWindow,
                DisposeSingleInstance,
            ],
            PresentStartupFailure,
            exitCode => Shutdown(exitCode));
    }

    private static void PresentStartupFailure(StartupFailureNotice notice)
    {
        _ = System.Windows.MessageBox.Show(
            $"错误代码：{notice.Code}\n\n{notice.UserMessage}",
            "历史粘贴板无法启动",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void DisposeTaskbarRestartRecovery()
    {
        var ownedResource = taskbarRestartRecovery;
        taskbarRestartRecovery = null;
        ownedResource?.Dispose();
    }

    private void DisposeTrayIcon()
    {
        var ownedResource = trayIcon;
        trayIcon = null;
        ownedResource?.Dispose();
    }

    private void DisposeWindow()
    {
        var ownedResource = window;
        window = null;
        if (ReferenceEquals(MainWindow, ownedResource))
        {
            MainWindow = null;
        }

        if (ownedResource is null)
        {
            return;
        }

        try
        {
            ownedResource.PrepareForExit();
        }
        finally
        {
            try
            {
                ownedResource.Close();
            }
            finally
            {
                ownedResource.Dispose();
            }
        }
    }

    private void DisposeSingleInstance()
    {
        var ownedResource = singleInstance;
        singleInstance = null;
        ownedResource?.Dispose();
    }

    private void ActivateExistingWindow()
    {
        _ = Dispatcher.InvokeAsync(() => window?.ShowFromTray());
    }

    private void ExitApplication()
    {
        if (isExiting)
        {
            return;
        }

        isExiting = true;
        DisposeTaskbarRestartRecovery();
        DisposeTrayIcon();
        window?.PrepareForExit();
        Shutdown();
    }
}
