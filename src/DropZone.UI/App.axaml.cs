using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using DropZone.Application.Services;
using DropZone.Domain.Interfaces;
using DropZone.Engine.Interop;
using DropZone.Infrastructure.Database;
using DropZone.Infrastructure.Platform;
using DropZone.Infrastructure.Storage;
using DropZone.UI.ViewModels;
using DropZone.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DropZone.UI;

public partial class App : Avalonia.Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
            var mainWindow = new MainWindow
            {
                DataContext = mainVm
            };
            desktop.MainWindow = mainWindow;

            // Wire desktop storage picker to Avalonia TopLevel StorageProvider
            var picker = (DesktopStoragePicker)_serviceProvider.GetRequiredService<IPlatformStoragePicker>();
            picker.RegisterHandlers(
                filePicker: async (title, allowMultiple) =>
                {
                    var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = title,
                        AllowMultiple = allowMultiple
                    });
                    return files.Select(f => f.Path.LocalPath).ToList();
                },
                folderPicker: async (title) =>
                {
                    var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        Title = title
                    });
                    return folders.Count > 0 ? folders[0].Path.LocalPath : null;
                },
                savePicker: async (title, defaultName, ext) =>
                {
                    var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = title,
                        SuggestedFileName = defaultName,
                        DefaultExtension = ext
                    });
                    return file?.Path.LocalPath;
                });

            desktop.Exit += async (_, _) =>
            {
                if (_serviceProvider != null)
                {
                    await _serviceProvider.DisposeAsync();
                }
            };

            StartBackgroundEngine(mainVm);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
            var mainView = new MainView
            {
                DataContext = mainVm
            };
            singleView.MainView = mainView;

            // Wire single-view storage picker when visual tree is attached
            mainView.Loaded += (_, _) =>
            {
                var topLevel = TopLevel.GetTopLevel(mainView);
                if (topLevel != null)
                {
                    var picker = (DesktopStoragePicker)_serviceProvider.GetRequiredService<IPlatformStoragePicker>();
                    picker.RegisterHandlers(
                        filePicker: async (title, allowMultiple) =>
                        {
                            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                            {
                                Title = title,
                                AllowMultiple = allowMultiple
                            });
                            return files.Select(f => f.Path.LocalPath).ToList();
                        },
                        folderPicker: async (title) =>
                        {
                            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                            {
                                Title = title
                            });
                            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
                        },
                        savePicker: async (title, defaultName, ext) =>
                        {
                            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                            {
                                Title = title,
                                SuggestedFileName = defaultName,
                                DefaultExtension = ext
                            });
                            return file?.Path.LocalPath;
                        });
                }
            };

            StartBackgroundEngine(mainVm);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartBackgroundEngine(MainViewModel mainVm)
    {
        _ = Task.Run(async () =>
        {
            if (_serviceProvider == null) return;
            var orchestrator = _serviceProvider.GetRequiredService<TransferOrchestrator>();
            var platform = _serviceProvider.GetRequiredService<IPlatformService>();
            var downloadDir = platform.GetStorageDirectory(StorageDirectoryType.Downloads);

            var config = new EngineConfig
            {
                DeviceId = Guid.NewGuid().ToString("N"),
                DeviceName = Environment.MachineName,
                DownloadDirectory = downloadDir,
                ListenPort = 52490
            };
            try
            {
                await orchestrator.InitializeAsync(config);
                await mainVm.InitializeAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[App] Init error: {ex.Message}");
            }
        });
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Platform services
        var platformService = new DesktopPlatformService();
        services.AddSingleton<IPlatformService>(platformService);
        services.AddSingleton<IPlatformWakeLockService, DesktopWakeLockService>();
        services.AddSingleton<IPlatformNotificationService, DesktopNotificationService>();
        services.AddSingleton<IPlatformLifecycleService, DesktopLifecycleService>();
        services.AddSingleton<IPlatformStoragePicker, DesktopStoragePicker>();

        var appData = platformService.GetStorageDirectory(StorageDirectoryType.AppData);
        var dbPath = Path.Combine(appData, "dropzone.db");
        var downloadDir = platformService.GetStorageDirectory(StorageDirectoryType.Downloads);

        // Security & Storage
        services.AddSingleton<ISecurityService, SecurityService>();
        services.AddSingleton<IFileSystemStorage>(sp => 
            new LocalFileSystemStorage(downloadDir, sp.GetRequiredService<ISecurityService>()));

        // Repositories & Backup
        services.AddSingleton<ITransferRepository>(_ => new SqliteTransferRepository(dbPath));
        services.AddSingleton<IDeviceRepository>(_ => new SqliteDeviceRepository(dbPath));
        services.AddSingleton<IBackupService, BackupService>();

        // Engine & Orchestrator
        services.AddSingleton<IDropZoneEngine, DropZoneEngineInterop>();
        services.AddSingleton<TransferQueue>();
        services.AddSingleton(sp => new TransferOrchestrator(
            sp.GetRequiredService<IDropZoneEngine>(),
            sp.GetRequiredService<ITransferRepository>(),
            sp.GetRequiredService<IDeviceRepository>(),
            sp.GetRequiredService<IFileSystemStorage>(),
            sp.GetRequiredService<ISecurityService>(),
            sp.GetRequiredService<TransferQueue>(),
            sp.GetRequiredService<IPlatformNotificationService>(),
            sp.GetRequiredService<IPlatformWakeLockService>(),
            sp.GetRequiredService<IPlatformLifecycleService>()
        ));

        // ViewModels
        services.AddSingleton(sp => new MainViewModel(
            sp.GetRequiredService<TransferOrchestrator>(),
            sp.GetRequiredService<IDeviceRepository>(),
            sp.GetRequiredService<ITransferRepository>(),
            sp.GetRequiredService<IFileSystemStorage>(),
            sp.GetRequiredService<ISecurityService>(),
            sp.GetRequiredService<IBackupService>(),
            downloadDir,
            sp.GetRequiredService<IPlatformService>(),
            sp.GetRequiredService<IPlatformStoragePicker>()
        ));
    }
}
