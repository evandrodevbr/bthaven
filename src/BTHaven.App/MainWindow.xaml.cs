using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using BTHaven.Windows.Diagnostics;

namespace BTHaven_App;

public sealed partial class MainWindow : Window
{
    private bool allowClose;
    private readonly TraceDiagnosticLogger logger = TraceDiagnosticLogger.Instance;

    public MainWindow()
    {
        InitializeComponent();
        logger.Info("App.MainWindow.Created");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Closing += AppWindow_Closing;
        Closed += MainWindow_Closed;

        RootFrame.Navigate(typeof(MainPage));
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        logger.Info("App.MainWindow.CloseRequested", new Dictionary<string, object?>
        {
            ["allowClose"] = allowClose,
        });
        if (allowClose)
        {
            return;
        }

        args.Cancel = true;
        AppWindow.Hide();
        TrayIcon.ShowNotification("BTHaven", "BTHaven continua em execução na bandeja do sistema.");
        logger.Info("App.MainWindow.HiddenToTray");
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        TrayIcon.Dispose();
        logger.Info("App.MainWindow.Closed");
    }

    private void TrayOpen_Click(object sender, RoutedEventArgs e)
    {
        logger.Info("App.Tray.OpenClicked");
        AppWindow.Show();
        Activate();
    }

    private async void TrayDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        logger.Info("App.Tray.DiagnosticsClicked");
        AppWindow.Show();
        Activate();
        if (RootFrame.Content is MainPage page)
        {
            await page.ShowDiagnosticsAsync();
        }
    }

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        logger.Info("App.Tray.ExitClicked");
        ExitApplication();
    }

    public void ExitApplication()
    {
        logger.Info("App.Exit.Requested");
        allowClose = true;
        TrayIcon.Dispose();
        Close();
    }
}
