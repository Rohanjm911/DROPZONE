using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DropZone.Application.Services;
using DropZone.Domain.Interfaces;
using DropZone.Engine.Interop;
using DropZone.Infrastructure.Database;
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
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainVm
            };

            desktop.Exit += async (_, _) =>
            {
                if (_serviceProvider != null)
                {
                    await _serviceProvider.DisposeAsync();
                }
            };

            // Start services in background
            _ = Task.Run(async () =>
            {
                var orchestrator = _serviceProvider.GetRequiredService<TransferOrchestrator>();
                var config = new EngineConfig
                {
                    DeviceId = Guid.NewGuid().ToString("N"),
                    DeviceName = Environment.MachineName,
                    DownloadDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "DropZone"),
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

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropZone");
        Directory.CreateDirectory(appData);

        var dbPath = Path.Combine(appData, "dropzone.db");
        var downloadDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "DropZone");
        Directory.CreateDirectory(downloadDir);

        // Security & Storage
        services.AddSingleton<ISecurityService, SecurityService>();
        services.AddSingleton<IFileSystemStorage>(sp => 
            new LocalFileSystemStorage(downloadDir, sp.GetRequiredService<ISecurityService>()));

        // Repositories
        services.AddSingleton<ITransferRepository>(_ => new SqliteTransferRepository(dbPath));
        services.AddSingleton<IDeviceRepository>(_ => new SqliteDeviceRepository(dbPath));

        // Engine & Orchestrator
        services.AddSingleton<IDropZoneEngine, DropZoneEngineInterop>();
        services.AddSingleton<TransferQueue>();
        services.AddSingleton<TransferOrchestrator>();

        // ViewModels
        services.AddSingleton(sp => new MainViewModel(
            sp.GetRequiredService<TransferOrchestrator>(),
            sp.GetRequiredService<IDeviceRepository>(),
            sp.GetRequiredService<ITransferRepository>(),
            sp.GetRequiredService<IFileSystemStorage>(),
            sp.GetRequiredService<ISecurityService>(),
            downloadDir
        ));
    }
}

