using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HAWinKiosk.Settings;

public partial class SettingsNumericInput : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(SettingsNumericInput),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    public static readonly DependencyProperty MaxLengthProperty =
        DependencyProperty.Register(nameof(MaxLength), typeof(int), typeof(SettingsNumericInput),
            new PropertyMetadata(5));

    public event EventHandler? TextChanged;

    public SettingsNumericInput()
    {
        InitializeComponent();
        InputBox.PreviewTextInput += (_, e) =>
        {
            e.Handled = e.Text.Any(c => !char.IsDigit(c));
        };
        System.Windows.DataObject.AddPastingHandler(InputBox, OnPaste);
        InputBox.TextChanged += (_, _) =>
        {
            var digits = DigitsOnly(InputBox.Text);
            if (digits != InputBox.Text)
            {
                var caret = InputBox.SelectionStart;
                InputBox.Text = digits;
                InputBox.SelectionStart = Math.Clamp(caret, 0, digits.Length);
                return;
            }

            SetCurrentValue(TextProperty, digits);
            TextChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    public bool TryGetInt(out int value) => int.TryParse(Text, out value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SettingsNumericInput ctrl) return;
        var next = DigitsOnly(e.NewValue as string ?? "");
        if (ctrl.InputBox.Text != next)
            ctrl.InputBox.Text = next;
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(System.Windows.DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var text = DigitsOnly(e.DataObject.GetData(System.Windows.DataFormats.Text) as string ?? "");
        if (string.IsNullOrEmpty(text))
        {
            e.CancelCommand();
            return;
        }

        e.CancelCommand();
        var start = InputBox.SelectionStart;
        var len = InputBox.SelectionLength;
        var current = InputBox.Text ?? "";
        var merged = current.Remove(start, len).Insert(start, text);
        merged = DigitsOnly(merged);
        if (MaxLength > 0 && merged.Length > MaxLength)
            merged = merged[..MaxLength];
        InputBox.Text = merged;
        InputBox.SelectionStart = Math.Min(start + text.Length, merged.Length);
    }

    private static string DigitsOnly(string s) => Regex.Replace(s ?? "", @"\D", "");
}
