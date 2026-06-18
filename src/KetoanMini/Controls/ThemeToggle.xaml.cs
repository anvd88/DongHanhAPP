using Wpf = System.Windows;
using WpfControls = System.Windows.Controls;

namespace KetoanMini;

public partial class ThemeToggle : WpfControls.UserControl
{
    public event EventHandler? ToggleRequested;

    public ThemeToggle()
    {
        InitializeComponent();
        RootButton.Click += (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty);
        RefreshVisual();
    }

    public void RefreshVisual()
    {
        Knob.Margin = ThemeState.IsDark ? new Wpf.Thickness(39, 3, 0, 0) : new Wpf.Thickness(3, 3, 0, 0);
        SunPath.Visibility = ThemeState.IsDark ? Wpf.Visibility.Collapsed : Wpf.Visibility.Visible;
        MoonPath.Visibility = ThemeState.IsDark ? Wpf.Visibility.Visible : Wpf.Visibility.Collapsed;
    }
}
