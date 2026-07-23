using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace DailyGate.Windows.Client;

public sealed class ShieldWindow : Window
{
    public ShieldWindow(System.Windows.Forms.Screen screen)
    {
        Left = screen.Bounds.Left; Top = screen.Bounds.Top; Width = screen.Bounds.Width; Height = screen.Bounds.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
        Background = new SolidColorBrush(MediaColor.FromRgb(14, 33, 31));
        Content = new StackPanel
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Children =
            {
                new Border { Width = 58, Height = 58, CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(MediaColor.FromRgb(36,180,133)), Child = new TextBlock { Text = "DG", FontSize = 19, FontWeight = FontWeights.Bold, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center } },
                new TextBlock { Text = "Ежедневный тест открыт на основном экране", Foreground = MediaBrushes.White, FontSize = 18, Margin = new Thickness(0,22,0,0) }
            }
        };
    }
}
