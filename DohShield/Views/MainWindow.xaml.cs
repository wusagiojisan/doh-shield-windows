using System.ComponentModel;
using System.Windows;
using DohShield.ViewModels;

namespace DohShield.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel mainVm, QueryLogViewModel queryLogVm, SettingsViewModel settingsVm)
    {
        InitializeComponent();

        HomeViewControl.DataContext = mainVm;
        QueryLogViewControl.DataContext = queryLogVm;
        SettingsViewControl.DataContext = settingsVm;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 縮小到 Tray（不真正關閉），由 TrayIconManager 的退出選單觸發真正關閉
        e.Cancel = true;
        Hide();
    }

    /// <summary>由 TrayIconManager 呼叫，真正關閉視窗並退出 App</summary>
    public void ForceClose()
    {
        Closing -= null;
        Application.Current.Shutdown();
    }
}
