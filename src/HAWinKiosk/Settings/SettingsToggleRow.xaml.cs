using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Controls.Primitives;

namespace HAWinKiosk.Settings;

public partial class SettingsToggleRow : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SettingsToggleRow),
            new PropertyMetadata("", (d, e) =>
            {
                if (d is SettingsToggleRow r)
                    r.LabelText.Text = e.NewValue as string ?? "";
            }));

    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(nameof(IsChecked), typeof(bool?), typeof(SettingsToggleRow),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) =>
            {
                if (d is SettingsToggleRow r)
                    r.Toggle.IsChecked = e.NewValue as bool?;
            }));

    public event RoutedEventHandler? Checked;
    public event RoutedEventHandler? Unchecked;

    public SettingsToggleRow()
    {
        InitializeComponent();
        Toggle.Checked += (_, e) =>
        {
            SetCurrentValue(IsCheckedProperty, true);
            Checked?.Invoke(this, e);
        };
        Toggle.Unchecked += (_, e) =>
        {
            SetCurrentValue(IsCheckedProperty, false);
            Unchecked?.Invoke(this, e);
        };
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool? IsChecked
    {
        get => (bool?)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public ToggleButton ToggleButton => Toggle;
}
