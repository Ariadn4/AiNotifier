using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace AiNotifier;

public partial class App : Application
{
    private static Mutex? _mutex;
    private static LocalizationService L => LocalizationService.Instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Initialize language before anything else
        var settings = SettingsManager.Load();
        var lang = settings.Language
            ?? (CultureInfo.CurrentUICulture.Name.StartsWith("zh") ? "zh-CN" : "en");
        L.SwitchLanguage(lang);

        // 全局异常处理，防止静默崩溃
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            MessageBox.Show(L.Get("Error_Fatal", ex?.Message ?? "", ex?.StackTrace ?? ""),
                L.Get("Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(L.Get("Error_General", args.Exception.Message, args.Exception.StackTrace ?? ""),
                L.Get("Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            _mutex = new Mutex(true, @"Global\AiNotifier_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show(L.Get("Error_AlreadyRunning"), "AI Notifier", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            MessageBox.Show(L.Get("Error_StartupFailed", ex.Message, ex.StackTrace ?? ""),
                L.Get("Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
