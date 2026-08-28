using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Markup;

namespace HAWinKiosk.Settings;

[ContentProperty(nameof(Items))]
public partial class SettingsSubPanel : System.Windows.Controls.UserControl
{
    public SettingsSubPanel()
    {
        InitializeComponent();
    }

    public UIElementCollection Items => ContentHost.Children;
}
