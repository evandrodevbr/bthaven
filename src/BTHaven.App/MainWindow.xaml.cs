using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using BTHaven.Windows.Diagnostics;

namespace BTHaven_App;

public sealed partial class MainWindow : Window
{
    private bool allowClose;
    private readonly TraceDiagnosticLogger logger = TraceDiagnosticLogger.Instance;

    public static bool IsShuttingDown { get; private set; }

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
        ShowFromTray();
    }

    private async void TrayDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        logger.Info("App.Tray.DiagnosticsClicked");
        ShowFromTray();
        if (RootFrame.Content is MainPage page)
        {
            await page.ShowDiagnosticsAsync();
        }
    }

    private void ShowFromTray()
    {
        // Activate() nao restaura janela minimizada; sem isso, "Abrir" pela bandeja
        // parece nao fazer nada quando a janela foi minimizada em vez de escondida.
        if (AppWindow.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }

        AppWindow.Show();
        Activate();
    }

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        logger.Info("App.Tray.ExitClicked");
        ExitApplication();
    }

    public async void ExitApplication()
    {
        logger.Info("App.Exit.Requested");
        IsShuttingDown = true;
        if (RootFrame.Content is MainPage page)
        {
            await page.ShutdownAsync();
        }

        allowClose = true;
        TrayIcon.Dispose();
        Close();
    }
}
