using System.IO;
using System.Windows;
using DohShield.Data;
using DohShield.Engine;
using DohShield.SystemTray;
using DohShield.ViewModels;
using DohShield.Views;

namespace DohShield;

public partial class App : Application
{
    private TrayIconManager? _trayIconManager;
    private MainViewModel? _mainViewModel;
    private WinDivertEngine? _engine;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // 捕捉所有啟動例外，寫入 crash.log 並彈出訊息框
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            var msg = ex.ExceptionObject?.ToString() ?? "未知錯誤";
            try { File.WriteAllText(CrashLogPath(), msg); } catch { /* ignore */ }
            MessageBox.Show(msg, "DohShield 啟動失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        try
        {
            base.OnStartup(e);

            // 初始化設定
            var settings = await AppSettings.LoadAsync();

            // 初始化資料庫
            var repository = new DnsLogRepository();
            await repository.InitializeAsync();

            // 初始化引擎
            _engine = new WinDivertEngine(repository, settings);

            // 初始化 ViewModel
            _mainViewModel = new MainViewModel(_engine, repository, settings);
            var queryLogVm = new QueryLogViewModel(repository);
            var settingsVm = new SettingsViewModel(settings, _engine);

            // 初始化主視窗
            var mainWindow = new MainWindow(_mainViewModel, queryLogVm, settingsVm);
            MainWindow = mainWindow;

            // 初始化系統 Tray
            _trayIconManager = new TrayIconManager(mainWindow, _engine);
            _trayIconManager.Initialize();

            mainWindow.Show();
        }
        catch (Exception ex)
        {
            var msg = ex.ToString();
            try { File.WriteAllText(CrashLogPath(), msg); } catch { /* ignore */ }
            MessageBox.Show(msg, "DohShield 啟動失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string CrashLogPath()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(dir, "crash.log");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 先停止 timer（避免 Dispatcher.Invoke 在關閉中拋出例外）
        _mainViewModel?.Dispose();

        // 同步停止引擎（GetAwaiter().GetResult() 可安全使用：StopAsync 不 Post 回 UI 執行緒）
        _engine?.StopAsync().GetAwaiter().GetResult();

        _trayIconManager?.Dispose();
        base.OnExit(e);

        // 確保 process 完全結束（避免殘留背景執行緒阻止檔案刪除）
        Environment.Exit(0);
    }
}
