using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace HAWinKiosk.Settings;

/// <summary>
/// Collapsible settings group. Expander subclass (not UserControl) so x:Name
/// children stay in the parent window namescope for code-behind.
/// </summary>
[ContentProperty(nameof(Items))]
public class SettingsSection : Expander
{
    private readonly StackPanel _body = new()
    {
        Margin = new Thickness(8, 8, 0, 4)
    };

    public SettingsSection()
    {
        IsExpanded = false;
        Content = _body;
        Margin = new Thickness(0, 0, 0, 8);
    }

    public UIElementCollection Items => _body.Children;
}
