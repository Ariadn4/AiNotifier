using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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
            // 忽略 WPF 渲染线程失败（多发生于 GPU 驱动复位/休眠唤醒后），
            // 切换到软件渲染避免再次崩溃
            if (IsRenderThreadFailure(args.Exception))
            {
                try { RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly; } catch { }
                // Existing window's composition surface is already busted — the render
                // mode switch only helps future windows. Force recreate the HwndTarget
                // by Hide/Show so the ball doesn't stay frozen/invisible.
                try
                {
                    if (Current?.MainWindow is Window w && w.IsLoaded)
                    {
                        w.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try { w.Hide(); w.Show(); } catch { }
                        }), DispatcherPriority.Background);
                    }
                }
                catch { }
                args.Handled = true;
                return;
            }

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

    private static bool IsRenderThreadFailure(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException!)
        {
            // UCEERR_RENDERTHREADFAILURE = 0x88980406
            if ((uint)e.HResult == 0x88980406) return true;
            if (e.InnerException == null) break;
        }
        return false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
