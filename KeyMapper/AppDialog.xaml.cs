using System.Windows;
using KeyMapper.Core;

namespace KeyMapper;

public partial class AppDialog : Window
{
    private AppDialog(
        Window owner,
        string title,
        string message,
        string? detail,
        string primaryText,
        string cancelText,
        bool showCancel)
    {
        InitializeComponent();
        Owner = owner;
        Icon = AppIcon.ImageSource;
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;
        CancelButton.Content = cancelText;
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(detail))
        {
            DetailBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            DetailText.Text = detail;
        }
    }

    public static bool Confirm(
        Window owner,
        string title,
        string message,
        string? detail,
        string primaryText = "确定",
        string cancelText = "取消")
    {
        var dialog = new AppDialog(owner, title, message, detail, primaryText, cancelText, true);
        return dialog.ShowDialog() == true;
    }

    public static void Alert(
        Window owner,
        string title,
        string message,
        string? detail = null,
        string primaryText = "知道了")
    {
        var dialog = new AppDialog(owner, title, message, detail, primaryText, "", false);
        dialog.ShowDialog();
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
