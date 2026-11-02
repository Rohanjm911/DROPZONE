using Avalonia;
using System;

namespace DropZone.UI;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                var crashLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dropzone_crash.log");
                File.WriteAllText(crashLog, $"[{DateTime.UtcNow:O}] FATAL CRASH: {ex}");
            }
            catch
            {
                // Ignore log write failure
            }
            Console.Error.WriteLine($"[DropZone Fatal] {ex}");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
