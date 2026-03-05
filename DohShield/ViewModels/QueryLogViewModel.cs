using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DohShield.Data;

namespace DohShield.ViewModels;

/// <summary>
/// 查詢紀錄 ViewModel：列表、搜尋、Type 過濾
/// </summary>
public sealed partial class QueryLogViewModel : ObservableObject
{
    private readonly DnsLogRepository _repository;

    private List<DnsQueryLog> _allLogs = [];

    [ObservableProperty]
    private ObservableCollection<DnsQueryLog> _displayLogs = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _selectedType = "全部";

    public IReadOnlyList<string> TypeFilters { get; } = ["全部", "A", "AAAA", "CNAME", "MX", "TXT", "HTTPS", "其他"];

    public QueryLogViewModel(DnsLogRepository repository)
    {
        _repository = repository;
    }

    /// <summary>從 DB 重新載入（由 UI 或引擎事件觸發）</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        var logs = await _repository.GetRecentLogsAsync(200);
        _allLogs = logs;
        ApplyFilter();
    }

    /// <summary>清除所有紀錄</summary>
    [RelayCommand]
    public async Task ClearLogsAsync()
    {
        await _repository.ClearAllAsync();
        _allLogs.Clear();
        ApplyFilter();
    }

    // 當搜尋文字或 type 過濾改變時重新過濾
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedTypeChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filtered = _allLogs.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(l => l.Domain.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        if (SelectedType != "全部")
        {
            filtered = SelectedType == "其他"
                ? filtered.Where(l => !IsKnownType(l.TypeName))
                : filtered.Where(l => l.TypeName == SelectedType);
        }

        var result = filtered.ToList();

        Application.Current.Dispatcher.Invoke(() =>
        {
            DisplayLogs = new ObservableCollection<DnsQueryLog>(result);
        });
    }

    private static bool IsKnownType(string typeName) =>
        typeName is "A" or "AAAA" or "CNAME" or "MX" or "TXT" or "HTTPS";
}
