using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace HAWinKiosk.Settings;

public partial class SettingsNote : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(SettingsNote),
            new PropertyMetadata("", (d, e) =>
            {
                if (d is SettingsNote n)
                    n.NoteText.Text = e.NewValue as string ?? "";
            }));

    public SettingsNote()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
