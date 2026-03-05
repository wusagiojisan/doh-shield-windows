using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DohShield.Core.Network;
using DohShield.Data;
using DohShield.Engine;

namespace DohShield.ViewModels;

/// <summary>
/// 設定 ViewModel：伺服器選擇、自訂 URL、儲存
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly WinDivertEngine _engine;

    [ObservableProperty]
    private string _selectedServerId;

    [ObservableProperty]
    private string _customServerUrl;

    [ObservableProperty]
    private string? _saveStatus;

    public IReadOnlyList<DohServerConfig> Presets => DohServers.Presets;

    public SettingsViewModel(AppSettings settings, WinDivertEngine engine)
    {
        _settings = settings;
        _engine = engine;
        _selectedServerId = settings.ServerId;
        _customServerUrl = settings.CustomServerUrl;
    }

    public bool IsCustomSelected
    {
        get => SelectedServerId == "custom";
        set { if (value) SelectedServerId = "custom"; }
    }

    partial void OnSelectedServerIdChanged(string value)
    {
        OnPropertyChanged(nameof(IsCustomSelected));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        // 驗證自訂 URL
        if (SelectedServerId == "custom")
        {
            if (string.IsNullOrWhiteSpace(CustomServerUrl) ||
                !Uri.TryCreate(CustomServerUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http"))
            {
                SaveStatus = "錯誤：請輸入有效的 HTTPS URL";
                return;
            }
        }

        _settings.ServerId = SelectedServerId;
        _settings.CustomServerUrl = CustomServerUrl.Trim();
        await _settings.SaveAsync();

        // 若攔截中，自動重啟套用新設定
        if (_engine.State == EngineState.Running)
        {
            SaveStatus = "套用中...";
            await _engine.StopAsync();
            await _engine.StartAsync();
            SaveStatus = "✓ 已套用新伺服器";
        }
        else
        {
            SaveStatus = "✓ 已儲存";
        }

        await Task.Delay(3000);
        SaveStatus = null;
    }
}
