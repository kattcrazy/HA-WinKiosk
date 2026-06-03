using System.Windows;
using System.Windows.Input;

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
        ThemePalette.Apply(Resources, dark);
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
