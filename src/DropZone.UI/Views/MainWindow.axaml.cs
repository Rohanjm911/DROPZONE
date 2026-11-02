using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DropZone.UI.ViewModels;

namespace DropZone.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var files = e.Data.GetFiles();
        if (files != null)
        {
            var paths = files.Select(f => f.Path.LocalPath).ToList();
            if (paths.Count > 0)
            {
                await vm.HandleDroppedFilesAsync(paths);
            }
        }
    }

    private async void OnBrowseFilesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select files to share with DropZone",
            AllowMultiple = true
        });

        if (files.Count > 0)
        {
            var paths = files.Select(f => f.Path.LocalPath).ToList();
            await vm.HandleDroppedFilesAsync(paths);
        }
    }
}

