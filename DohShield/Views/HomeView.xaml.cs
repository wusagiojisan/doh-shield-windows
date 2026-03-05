using System.Windows;
using System.Windows.Controls;

namespace DohShield.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        Loaded += HomeView_Loaded;
    }

    private void HomeView_Loaded(object sender, RoutedEventArgs e)
    {
        // 加入 NullToVisibilityConverter 至 Resources（若 App.xaml 中未定義）
        if (!Resources.Contains("NullToVisibilityConverter"))
            Resources.Add("NullToVisibilityConverter", new NullToVisibilityConverter());
    }
}

/// <summary>null → Collapsed，非 null → Visible</summary>
internal sealed class NullToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value == null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
