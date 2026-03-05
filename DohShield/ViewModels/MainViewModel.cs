using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DohShield.Data;
using DohShield.Engine;

namespace DohShield.ViewModels;

/// <summary>
/// 首頁 ViewModel：開關按鈕、統計數字、連線時間
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly WinDivertEngine _engine;
    private readonly DnsLogRepository _repository;
    private readonly AppSettings _settings;

    private DateTime? _connectedAt;
    private readonly System.Timers.Timer _timer;

    [ObservableProperty]
    private EngineState _engineState = EngineState.Stopped;

    [ObservableProperty]
    private string _statusText = "已停止";

    [ObservableProperty]
    private string _serverDisplayName = "";

    [ObservableProperty]
    private long _totalQueries;

    [ObservableProperty]
    private long _cachedQueries;

    [ObservableProperty]
    private double _avgResponseMs;

    [ObservableProperty]
    private string _connectionDuration = "--:--";

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsRunning => EngineState == EngineState.Running;
    public bool IsStopped => EngineState == EngineState.Stopped;
    public bool IsBusy  => EngineState is EngineState.Starting;

    public MainViewModel(WinDivertEngine engine, DnsLogRepository repository, AppSettings settings)
    {
        _engine = engine;
        _repository = repository;
        _settings = settings;

        ServerDisplayName = settings.ActiveServerDisplayName;

        _engine.StateChanged += OnEngineStateChanged;
        _engine.QueryLogged += OnQueryLogged;
        _engine.ErrorOccurred += OnErrorOccurred;

        // 每秒更新連線時間與統計
        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += (_, _) => UpdateStats();
        _timer.AutoReset = true;
        _timer.Start();
    }

    // ──────── 開關指令 ────────

    [RelayCommand(CanExecute = nameof(IsStopped))]
    private async Task Start()
    {
        ErrorMessage = null;
        ServerDisplayName = _settings.ActiveServerDisplayName;
        await _engine.StartAsync();
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task Stop()
    {
        await _engine.StopAsync();
    }

    // ──────── 引擎事件 ────────

    private void OnEngineStateChanged(object? sender, EngineState state)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            EngineState = state;
            StatusText = state switch
            {
                EngineState.Running  => "已啟動",
                EngineState.Starting => "啟動中...",
                EngineState.Stopped  => "已停止",
                EngineState.Error    => "錯誤",
                _                    => ""
            };

            if (state == EngineState.Running)
                _connectedAt = DateTime.Now;
            else
                _connectedAt = null;

            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsStopped));
            OnPropertyChanged(nameof(IsBusy));
            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        });
    }

    private void OnQueryLogged(object? sender, Data.DnsQueryLog log)
    {
        // 更新在 timer 回呼，避免 UI 過度刷新
    }

    private void OnErrorOccurred(object? sender, string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ErrorMessage = message;
        });
    }

    // ──────── 定時更新 ────────

    private void UpdateStats()
    {
        var app = Application.Current;
        if (app == null || app.Dispatcher.HasShutdownStarted) return;

        app.Dispatcher.Invoke(() =>
        {
            TotalQueries = _engine.TotalQueries;
            CachedQueries = _engine.CachedQueries;
            AvgResponseMs = Math.Round(_engine.AvgResponseTimeMs, 0);

            if (_connectedAt.HasValue)
            {
                var elapsed = DateTime.Now - _connectedAt.Value;
                ConnectionDuration = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            }
            else
            {
                ConnectionDuration = "--:--";
            }
        });
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _engine.StateChanged -= OnEngineStateChanged;
        _engine.QueryLogged  -= OnQueryLogged;
        _engine.ErrorOccurred -= OnErrorOccurred;
    }
}
