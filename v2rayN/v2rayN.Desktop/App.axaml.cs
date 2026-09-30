using v2rayN.Desktop.Common;
using v2rayN.Desktop.Views;

namespace v2rayN.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime previewDesktop)
        {
            var args = previewDesktop.Args ?? [];
            if (args.Any(arg => string.Equals(arg, "--dirac-ui-preview", StringComparison.OrdinalIgnoreCase)))
            {
                static string Option(string[] values, string name)
                    => values.FirstOrDefault(value => value.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                        ?.Substring(name.Length + 1) ?? "";

                static double Dimension(string text, double fallback, double min, double max)
                    => double.TryParse(text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var number)
                        ? Math.Clamp(number, min, max) : fallback;

                var width = Dimension(Option(args, "--dirac-preview-width"), 550, 550, 1000);
                var height = Dimension(Option(args, "--dirac-preview-height"), 610, 610, 1000);
                var page = Option(args, "--dirac-preview-page").ToLowerInvariant();
                if (page is not ("home" or "routing" or "updates" or "settings"))
                {
                    page = "home";
                }

                var view = new DiracHomeView();
                if (!args.Any(arg => string.Equals(arg, "--dirac-preview-no-profile", StringComparison.OrdinalIgnoreCase)))
                {
                    view.SetPreviewProfile("[Custom] Dirac");
                }
                view.SetRouting(russiaDirect: true, canChange: true, isActive: true);
                view.SetUpdateChannel(beta: false, busy: false);
                var stateText = Option(args, "--dirac-preview-state");
                if (!Enum.TryParse<DiracConnectionDisplay>(stateText, ignoreCase: true, out var state))
                {
                    state = DiracConnectionDisplay.Connected;
                }
                view.SetConnectionState(state);
                view.SetRouting(russiaDirect: true, canChange: true,
                    isActive: state == DiracConnectionDisplay.Connected);
                view.SetNetworkHealth(state == DiracConnectionDisplay.Connected
                    ? DiracNetworkHealth.Available : DiracNetworkHealth.Inactive,
                    state == DiracConnectionDisplay.Connected ? 68 : null);
                view.SetTraffic(state == DiracConnectionDisplay.Connected ? 1327104 : null,
                    state == DiracConnectionDisplay.Connected ? 65536 : null);
                view.ShowSection(page);

                previewDesktop.MainWindow = new Window
                {
                    Title = "Dirac Desktop UI Preview (offline)",
                    Width = width,
                    Height = height,
                    MinWidth = 550,
                    MinHeight = 610,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = view
                };
                if (state == DiracConnectionDisplay.Connected)
                {
                    var previewSeconds = 0;
                    var previewTimer = new Avalonia.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1)
                    };
                    previewTimer.Tick += (_, _) =>
                    {
                        previewSeconds++;
                        var phase = previewSeconds % 15;
                        if (phase == 0)
                        {
                            view.SetNetworkHealth(DiracNetworkHealth.Checking);
                        }
                        else if (phase == 2)
                        {
                            view.SetNetworkHealth(DiracNetworkHealth.Available, 55 + previewSeconds % 45);
                        }
                        else if (phase > 2)
                        {
                            view.SetNetworkCheckAge(TimeSpan.FromSeconds(phase - 2));
                        }

                        view.SetTraffic(800_000 + previewSeconds * 211_111L % 900_000,
                            28_000 + previewSeconds * 17_333L % 85_000);
                    };
                    view.NetworkCheckRequested += (_, _) =>
                    {
                        view.SetNetworkHealth(DiracNetworkHealth.Checking);
                        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                        {
                            previewSeconds = 2;
                            view.SetNetworkHealth(DiracNetworkHealth.Available, 68);
                        }, TimeSpan.FromMilliseconds(700));
                    };
                    previewDesktop.MainWindow.Closed += (_, _) => previewTimer.Stop();
                    previewTimer.Start();
                }
                base.OnFrameworkInitializationCompleted();
                return;
            }
        }

        var viewLocator = SimpleViewLocator.Instance;
        DataTemplates.Add(viewLocator);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!Design.IsDesignMode)
            {
                AppManager.Instance.InitComponents();
                DataContext = StatusBarViewModel.Instance;
            }

            var mainWindowViewModel = new MainWindowViewModel();
            var mainWindow = (MainWindow)viewLocator.Build(mainWindowViewModel);
            mainWindow.ViewModel = mainWindowViewModel;
            desktop.MainWindow = mainWindow;

            if (OperatingSystem.IsMacOS())
            {
                Current?.TryGetFeature<IActivatableLifetime>()?.Activated += OnMacOSActivated;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    #region MacOS Activation

    private void OnMacOSActivated(object? sender, ActivatedEventArgs args)
    {
        if (args.Kind != ActivationKind.Reopen)
        {
            return;
        }

        if ((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        var isMiniaturized = MacAppUtils.IsWindowMiniaturized(mainWindow);

        Dispatcher.UIThread.Post(() =>
        {
            if (isMiniaturized)
            {
                RestoreMacOSAccessoryPolicyAfterMiniaturize(mainWindow);
                mainWindow.ShowHideWindow(true);
                return;
            }

            if (!AppManager.Instance.Config.UiItem.MacOSShowInDock)
            {
                MacAppUtils.SetActivationPolicyAccessory();
            }

            mainWindow.ShowHideWindow(true);
        });
    }

    private static void RestoreMacOSAccessoryPolicyAfterMiniaturize(MainWindow mainWindow)
    {
        if (AppManager.Instance.Config.UiItem.MacOSShowInDock)
        {
            return;
        }

        mainWindow
            .GetObservable(Window.WindowStateProperty)
            .Skip(1)
            .Where(state => state != WindowState.Minimized)
            .Take(1)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => QueueMacOSAccessoryPolicyRestore(mainWindow));
    }

    private static void QueueMacOSAccessoryPolicyRestore(MainWindow mainWindow)
    {
        // AppKit may keep isMiniaturized set until the Dock restore animation finishes.
        DispatcherTimer.RunOnce(() => RestoreMacOSAccessoryPolicy(mainWindow), TimeSpan.FromMilliseconds(300));
    }

    private static void RestoreMacOSAccessoryPolicy(MainWindow mainWindow)
    {
        if (AppManager.Instance.Config.UiItem.MacOSShowInDock || MacAppUtils.IsWindowMiniaturized(mainWindow))
        {
            return;
        }

        MacAppUtils.SetActivationPolicyAccessory();
        mainWindow.Activate();
        mainWindow.Focus();
    }

    #endregion MacOS Activation

    #region App Event

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject != null)
        {
            Logging.SaveLog("CurrentDomain_UnhandledException", (Exception)e.ExceptionObject);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logging.SaveLog("TaskScheduler_UnobservedTaskException", e.Exception);
    }

    private async void MenuAddServerViaClipboardClick(object? sender, EventArgs e)
    {
        try
        {
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: not null })
            {
                AppEvents.AddServerViaClipboardRequested.Publish();
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("MenuAddServerViaClipboardClick", ex);
        }
    }

    private async void MenuExit_Click(object? sender, EventArgs e)
    {
        await AppManager.Instance.AppExitAsync(false);
        AppManager.Instance.Shutdown(true);
    }

    #endregion App Event
}
