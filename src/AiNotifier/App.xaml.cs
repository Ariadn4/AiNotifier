using System.Windows;
using System.Windows.Threading;

namespace AiNotifier;

public partial class App : Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局异常处理，防止静默崩溃
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            MessageBox.Show($"发生致命错误：\n{ex?.Message}\n\n{ex?.StackTrace}",
                "AI Notifier 错误", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"发生错误：\n{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "AI Notifier 错误", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            _mutex = new Mutex(true, @"Global\AiNotifier_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("AI Notifier 已在运行。", "AI Notifier", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动失败：\n{ex.Message}\n\n{ex.StackTrace}",
                "AI Notifier 错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
