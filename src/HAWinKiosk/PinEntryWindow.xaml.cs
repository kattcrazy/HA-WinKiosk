using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace HAWinKiosk;

public partial class PinEntryWindow : Window
{
    private readonly string _expectedPin;
    private readonly string _expectedResetAnswer;
    private readonly bool _canResetPin;

    public string Pin => PinBox.Password;
    public bool PinResetRequested { get; private set; }

    public PinEntryWindow(string? pinHint, string? expectedPin, string? resetQuestion, string? resetAnswer, string? uiThemeMode = null)
    {
        InitializeComponent();
        _expectedPin = expectedPin ?? "";
        _expectedResetAnswer = resetAnswer?.Trim() ?? "";
        _canResetPin = !string.IsNullOrWhiteSpace(resetQuestion) && !string.IsNullOrWhiteSpace(_expectedResetAnswer);
        ApplyPinTheme(uiThemeMode);
        if (!string.IsNullOrWhiteSpace(pinHint))
        {
            HintBlock.Text = "Hint: " + pinHint;
            HintBlock.Visibility = Visibility.Visible;
        }
        if (_canResetPin)
        {
            ResetQuestionBlock.Text = "Security question: " + (resetQuestion?.Trim() ?? "");
        }
    }

    private void ApplyPinTheme(string? uiThemeMode)
    {
        var dark = UiThemeHelper.ResolveEffectiveDark(uiThemeMode);
        var r = Resources;

        static SolidColorBrush B(byte r, byte g, byte b) => new(System.Windows.Media.Color.FromRgb(r, g, b));

        if (dark)
        {
            r["Theme.Pin.CardBg"] = B(0x25, 0x25, 0x26);
            r["Theme.Pin.CardBorder"] = B(0x3F, 0x3F, 0x46);
            r["Theme.Pin.Fg"] = B(0xE8, 0xE8, 0xE8);
            r["Theme.Pin.FgMuted"] = B(0xB0, 0xB0, 0xB0);
            r["Theme.Settings.InputBg"] = B(0x30, 0x30, 0x30);
            r["Theme.Settings.InputBorder"] = B(0x50, 0x50, 0x50);
            r["Theme.Settings.Fg"] = B(0xE8, 0xE8, 0xE8);
            r["Theme.Settings.FgMuted"] = B(0xB0, 0xB0, 0xB0);
            r["Theme.Button.SecondaryBg"] = B(0x3A, 0x3A, 0x3D);
            r["Theme.Button.SecondaryFg"] = B(0xE0, 0xE0, 0xE0);
            r["Theme.Button.SecondaryBorder"] = B(0x5A, 0x5A, 0x60);
        }
        else
        {
            r["Theme.Pin.CardBg"] = B(0xF4, 0xF6, 0xF9);
            r["Theme.Pin.CardBorder"] = B(0xD0, 0xD8, 0xE0);
            r["Theme.Pin.Fg"] = B(0x1A, 0x1A, 0x1A);
            r["Theme.Pin.FgMuted"] = B(0x55, 0x55, 0x55);
            r["Theme.Settings.InputBg"] = B(0xFF, 0xFF, 0xFF);
            r["Theme.Settings.InputBorder"] = B(0xCC, 0xCC, 0xCC);
            r["Theme.Settings.Fg"] = B(0x1A, 0x1A, 0x1A);
            r["Theme.Settings.FgMuted"] = B(0x44, 0x44, 0x44);
            r["Theme.Button.SecondaryBg"] = B(0xF6, 0xF8, 0xFB);
            r["Theme.Button.SecondaryFg"] = B(0x1F, 0x29, 0x37);
            r["Theme.Button.SecondaryBorder"] = B(0xC8, 0xD2, 0xDE);
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PinBox.Focus();
    }

    private void PinBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ErrorBlock.Visibility = Visibility.Collapsed;
        ErrorWithResetBlock.Visibility = Visibility.Collapsed;
    }

    private void ResetPinLink_Click(object sender, RoutedEventArgs e)
    {
        ResetPinPanel.Visibility = Visibility.Visible;
        ResetAnswerBox.Focus();
    }

    private void SubmitResetAnswer_Click(object sender, RoutedEventArgs e)
    {
        var answer = (ResetAnswerBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(_expectedResetAnswer) || !string.Equals(answer, _expectedResetAnswer, StringComparison.OrdinalIgnoreCase))
        {
            ErrorBlock.Text = "Reset answer is incorrect";
            ErrorBlock.Visibility = Visibility.Visible;
            ErrorWithResetBlock.Visibility = Visibility.Collapsed;
            ResetAnswerBox.Focus();
            ResetAnswerBox.SelectAll();
            return;
        }

        PinResetRequested = true;
        DialogResult = true;
        Close();
    }

    private void TrySubmit()
    {
        if (PinBox.Password != _expectedPin)
        {
            ErrorBlock.Visibility = _canResetPin ? Visibility.Collapsed : Visibility.Visible;
            ErrorWithResetBlock.Visibility = _canResetPin ? Visibility.Visible : Visibility.Collapsed;
            PinBox.Focus();
            PinBox.SelectAll();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        TrySubmit();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void PinBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TrySubmit();
        }
    }
}
