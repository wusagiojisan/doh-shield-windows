using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace DohShield.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Resources.Add("IdMatchConverter", new ServerIdMatchConverter());
        Resources.Add("BoolToVisConverter", new BoolToVisibilityConverter());
    }
}

/// <summary>
/// 將 SelectedServerId (string) 與 ConverterParameter (string) 比對，
/// 用於 RadioButton 雙向繫結
/// </summary>
internal sealed class ServerIdMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true) return parameter?.ToString() ?? "";
        return Binding.DoNothing;
    }
}

/// <summary>bool → Visibility</summary>
internal sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}
