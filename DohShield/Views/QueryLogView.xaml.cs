using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DohShield.ViewModels;

namespace DohShield.Views;

public partial class QueryLogView : UserControl
{
    public QueryLogView()
    {
        InitializeComponent();

        Resources.Add("BoolToHitConverter", new BoolToHitConverter());

        // Tab 切換時自動刷新
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true && DataContext is QueryLogViewModel vm)
                await vm.RefreshAsync();
        };
    }
}

/// <summary>bool → "✓" / ""</summary>
internal sealed class BoolToHitConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "✓" : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
