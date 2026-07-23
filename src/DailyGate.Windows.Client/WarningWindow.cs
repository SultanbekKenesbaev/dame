using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace DailyGate.Windows.Client;

public sealed class WarningWindow : Window
{
    private readonly TextBlock _message;

    public WarningWindow()
    {
        Width = 470; Height = 150; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Topmost = true; ShowInTaskbar = false; Background = new SolidColorBrush(MediaColor.FromRgb(14, 33, 31));
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.WorkArea.Right - Width - 22; Top = SystemParameters.WorkArea.Bottom - Height - 22;
        var grid = new Grid { Margin = new Thickness(22) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(MediaColor.FromRgb(232, 166, 59)), Child = new TextBlock { Text = "!", FontSize = 20, FontWeight = FontWeights.Bold, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center } };
        _message = new TextBlock { Foreground = MediaBrushes.White, FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        Grid.SetColumn(_message, 1); grid.Children.Add(badge); grid.Children.Add(_message); Content = grid;
    }

    public void UpdateMessage(string message) { _message.Text = message; if (!IsVisible) Show(); }
}
