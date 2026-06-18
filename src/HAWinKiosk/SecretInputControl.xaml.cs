using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HAWinKiosk;

public partial class SecretInputControl : System.Windows.Controls.UserControl
{
    private bool _syncing;
    private int _caretIndex;

    public SecretInputControl()
    {
        InitializeComponent();
        HiddenInput.KeyDown += ForwardKeyDown;
        VisibleInput.KeyDown += ForwardKeyDown;
    }

    public event EventHandler? TextChanged;
    public event System.Windows.Input.KeyEventHandler? InputKeyDown;

    public string Text
    {
        get => RevealToggle.IsChecked == true ? VisibleInput.Text : HiddenInput.Password;
        set
        {
            _syncing = true;
            try
            {
                var text = value ?? "";
                HiddenInput.Password = text;
                VisibleInput.Text = text;
                _caretIndex = text.Length;
            }
            finally
            {
                _syncing = false;
            }
        }
    }

    public new void Focus()
    {
        if (RevealToggle.IsChecked == true)
            VisibleInput.Focus();
        else
            HiddenInput.Focus();
    }

    public void SelectAll()
    {
        if (RevealToggle.IsChecked == true)
            VisibleInput.SelectAll();
        else
            HiddenInput.SelectAll();
    }

    private void RevealToggle_Changed(object sender, RoutedEventArgs e)
    {
        var revealed = RevealToggle.IsChecked == true;
        _syncing = true;
        try
        {
            if (revealed)
            {
                var text = HiddenInput.Password;
                VisibleInput.Text = text;
                VisibleInput.Visibility = Visibility.Visible;
                HiddenInput.Visibility = Visibility.Collapsed;
                SetEyeIcon(revealed: true);
                VisibleInput.Focus();
                var caret = Math.Clamp(_caretIndex, 0, text.Length);
                VisibleInput.SelectionStart = caret;
                VisibleInput.SelectionLength = 0;
            }
            else
            {
                _caretIndex = VisibleInput.SelectionStart;
                var text = VisibleInput.Text;
                HiddenInput.Password = text;
                HiddenInput.Visibility = Visibility.Visible;
                VisibleInput.Visibility = Visibility.Collapsed;
                SetEyeIcon(revealed: false);
                HiddenInput.Focus();
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SetEyeIcon(bool revealed)
    {
        if (revealed)
        {
            EyeOpenIcon.Visibility = Visibility.Collapsed;
            EyeSlashIcon.Visibility = Visibility.Visible;
        }
        else
        {
            EyeSlashIcon.Visibility = Visibility.Collapsed;
            EyeOpenIcon.Visibility = Visibility.Visible;
            EyeOpenIcon.Foreground = (System.Windows.Media.Brush)FindResource("Theme.SecretInput.EyeMuted");
        }
    }

    private void VisibleInput_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || RevealToggle.IsChecked != true) return;
        _caretIndex = VisibleInput.SelectionStart;
    }

    private void ForwardKeyDown(object sender, System.Windows.Input.KeyEventArgs e) => InputKeyDown?.Invoke(this, e);

    private void HiddenInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (RevealToggle.IsChecked != true)
        {
            _caretIndex = HiddenInput.Password.Length;
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void VisibleInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        if (RevealToggle.IsChecked == true)
            TextChanged?.Invoke(this, EventArgs.Empty);
    }
}
