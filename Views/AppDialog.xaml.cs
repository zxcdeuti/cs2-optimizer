using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CS2Optimizer.Views;

public enum DialogChoice { Cancel, Primary, Secondary }

/// <summary>
/// The app's own confirmation window. Anything that isn't an explicit button press (Esc, Alt+F4, the owner closing)
/// counts as Cancel, and dangerous questions start with focus on Cancel so a stray Enter can't confirm them.
/// </summary>
public partial class AppDialog : Window
{
    public DialogChoice Choice { get; private set; } = DialogChoice.Cancel;

    public AppDialog(string title, string message, string primary, string? secondary, string? cancel, bool danger, IReadOnlyCollection<string>? items)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        MessageText.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PrimaryBtn.Content = primary;

        SecondaryBtn.Content = secondary;
        SecondaryBtn.Visibility = secondary != null ? Visibility.Visible : Visibility.Collapsed;
        CancelBtn.Content = cancel;
        CancelBtn.Visibility = cancel != null ? Visibility.Visible : Visibility.Collapsed;

        if (items is { Count: > 0 })
        {
            Items.ItemsSource = items;
            Items.Visibility = Visibility.Visible;
        }

        Eyebrow.Text = danger ? "Внимание" : cancel == null ? "CS2 Optimizer" : "Подтверждение";
        var accent = (Brush)FindResource(danger ? "DangerBrush" : "AccentBrush");
        Eyebrow.Foreground = accent;
        Strip.Fill = accent;
        if (danger) PrimaryBtn.Style = (Style)FindResource("DangerButton");

        Loaded += (_, _) => (danger && cancel != null ? CancelBtn : PrimaryBtn).Focus();
    }

    /// <summary>Asks a question. Works with a hidden owner too (called from the tray): the dialog then stands on its own.</summary>
    public static DialogChoice Ask(Window? owner, string title, string message, string primary, string? secondary = null,
        string? cancel = "Отмена", bool danger = false, IReadOnlyCollection<string>? items = null)
    {
        var dialog = new AppDialog(title, message, primary, secondary, cancel, danger, items);
        if (owner is { IsVisible: true } && owner.WindowState != WindowState.Minimized)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true;
            dialog.Topmost = true;
        }
        dialog.ShowDialog();
        return dialog.Choice;
    }

    public static void Inform(Window? owner, string title, string message) =>
        Ask(owner, title, message, "Понятно", cancel: null);

    void Finish(DialogChoice choice)
    {
        Choice = choice;
        Close();
    }

    void Primary_Click(object sender, RoutedEventArgs e) => Finish(DialogChoice.Primary);
    void Secondary_Click(object sender, RoutedEventArgs e) => Finish(DialogChoice.Secondary);
    void Cancel_Click(object sender, RoutedEventArgs e) => Finish(DialogChoice.Cancel);

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Finish(DialogChoice.Cancel);
    }

    void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
