using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace BTHaven_App;

public sealed partial class MainWindow : Window
{
    private bool allowClose;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Closing += AppWindow_Closing;
        Closed += MainWindow_Closed;

        RootFrame.Navigate(typeof(MainPage));
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (allowClose)
        {
            return;
        }

        args.Cancel = true;
        AppWindow.Hide();
        TrayIcon.ShowNotification("BTHaven", "BTHaven continua em execução na bandeja do sistema.");
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        TrayIcon.Dispose();
    }

    private void TrayOpen_Click(object sender, RoutedEventArgs e)
    {
        AppWindow.Show();
        Activate();
    }

    private async void TrayDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        AppWindow.Show();
        Activate();
        if (RootFrame.Content is MainPage page)
        {
            await page.ShowDiagnosticsAsync();
        }
    }

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        ExitApplication();
    }

    public void ExitApplication()
    {
        allowClose = true;
        TrayIcon.Dispose();
        Close();
    }
}
