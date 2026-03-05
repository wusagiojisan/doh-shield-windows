using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using DohShield.Engine;
using DohShield.Views;

namespace DohShield.SystemTray;

/// <summary>
/// System Tray 管理：TaskbarIcon（Hardcodet.NotifyIcon.Wpf）
/// 最小化時隱藏主視窗，雙擊恢復；退出時先停止 WinDivertEngine
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly MainWindow _mainWindow;
    private readonly WinDivertEngine _engine;
    private TaskbarIcon? _trayIcon;

    // 攔截中：藍色；已停止：灰色
    private readonly Icon _iconRunning = CreateStateIcon(Color.FromArgb(0x21, 0x96, 0xF3));
    private readonly Icon _iconStopped = CreateStateIcon(Color.FromArgb(0x9E, 0x9E, 0x9E));

    public TrayIconManager(MainWindow mainWindow, WinDivertEngine engine)
    {
        _mainWindow = mainWindow;
        _engine = engine;
    }

    public void Initialize()
    {
        _trayIcon = new TaskbarIcon
        {
            Icon = _iconStopped,
            ToolTipText = "DOH Shield — 已停止",
            Visibility = Visibility.Visible
        };

        // 雙擊恢復主視窗
        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();

        // 右鍵選單
        var menu = BuildContextMenu();
        _trayIcon.ContextMenu = menu;

        // 監聽引擎狀態，更新圖示與 tooltip
        _engine.StateChanged += (_, state) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _trayIcon.Icon = state == EngineState.Running ? _iconRunning : _iconStopped;
                _trayIcon.ToolTipText = state == EngineState.Running
                    ? "DOH Shield — 攔截中"
                    : "DOH Shield — 已停止";
            });
        };
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var showItem = new MenuItem { Header = "顯示主視窗" };
        showItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(showItem);

        menu.Items.Add(new Separator());

        var toggleItem = new MenuItem { Header = "啟動攔截" };
        toggleItem.Click += async (_, _) =>
        {
            if (_engine.State == EngineState.Running)
                await _engine.StopAsync();
            else
                await _engine.StartAsync();
        };

        _engine.StateChanged += (_, state) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                toggleItem.Header = state == EngineState.Running ? "停止攔截" : "啟動攔截");
        };

        menu.Items.Add(toggleItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "退出" };
        exitItem.Click += async (_, _) =>
        {
            await _engine.StopAsync();
            _mainWindow.ForceClose();
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ShowMainWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    /// <summary>
    /// 建立 16×16 圓形狀態圖示：指定顏色背景 + 白色「D」字
    /// </summary>
    private static Icon CreateStateIcon(Color bgColor)
    {
        using var bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var brush = new SolidBrush(bgColor);
            g.FillEllipse(brush, 0, 0, 15, 15);

            using var font = new Font("Arial", 7.5f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString("D", font, Brushes.White, 2.5f, 2.5f);
        }

        // Clone 讓 Icon 自行管理記憶體，再清除原始 HICON
        IntPtr hicon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hicon).Clone();
        NativeMethods.DestroyIcon(hicon);
        return icon;
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        _iconRunning.Dispose();
        _iconStopped.Dispose();
    }
}

/// <summary>呼叫 Win32 DestroyIcon 清除 HICON 資源</summary>
internal static partial class NativeMethods
{
    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(IntPtr hIcon);
}
