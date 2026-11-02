using Avalonia.Controls;
using Avalonia.Interactivity;
using DropZone.UI.ViewModels;

namespace DropZone.UI.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private async void OnExportBackupClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var box = this.FindControl<TextBox>("BackupPassphraseBox");
        var passphrase = box?.Text ?? string.Empty;
        await vm.ExportEncryptedBackupAsync(passphrase);
    }
}

