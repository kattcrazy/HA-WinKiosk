using System.Collections;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Markup;

namespace HAWinKiosk.Settings;

[ContentProperty(nameof(Items))]
public partial class SettingsSelectRow : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SettingsSelectRow),
            new PropertyMetadata("", (d, e) =>
            {
                if (d is SettingsSelectRow r)
                    r.LabelText.Text = e.NewValue as string ?? "";
            }));

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(SettingsSelectRow),
            new PropertyMetadata(null, (d, e) =>
            {
                if (d is SettingsSelectRow r)
                    r.Combo.ItemsSource = e.NewValue as IEnumerable;
            }));

    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(SettingsSelectRow),
            new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) =>
            {
                if (d is SettingsSelectRow r && e.NewValue is int i && r.Combo.SelectedIndex != i)
                    r.Combo.SelectedIndex = i;
            }));

    public event SelectionChangedEventHandler? SelectionChanged;

    public SettingsSelectRow()
    {
        InitializeComponent();
        Combo.SelectionChanged += (_, e) =>
        {
            SetCurrentValue(SelectedIndexProperty, Combo.SelectedIndex);
            SelectionChanged?.Invoke(this, e);
        };
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public System.Windows.Controls.ComboBox ComboBox => Combo;

    public ItemCollection Items => Combo.Items;

    public object? SelectedItem
    {
        get => Combo.SelectedItem;
        set => Combo.SelectedItem = value;
    }
}
