using Avalonia.Controls.Notifications;
using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Manager;
using v2rayN.Desktop.Services;

namespace v2rayN.Desktop.Views;

public partial class MainWindow : WindowBase<MainWindowViewModel>
{
    private static Config _config;
    private readonly WindowNotificationManager? _manager;
    private bool _blCloseByUser = false;
    private readonly DiracAppUpdateService _diracUpdates = new();
    private bool _diracUpdateBusy;
    private bool _diracPowerBusy;
    private DateTime? _diracConnectStartedUtc;
    private readonly Avalonia.Threading.DispatcherTimer _diracDashboardTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    public MainWindow()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this)) { MaxItems = 2, Position = NotificationPosition.TopRight };
        WireDiracDashboard();

        this.WhenActivated(disposables =>
        {
            ViewModel.ReadTextFromClipboardInteraction.RegisterHandler(async interaction =>
            {
                var result = await AvaUtils.GetClipboardData(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.BrowseDiracProfileFileInteraction.RegisterHandler(async interaction =>
            {
                var jsonFilter = new Avalonia.Platform.Storage.FilePickerFileType("Dirac JSON profile")
                {
                    Patterns = ["*.json"]
                };
                var file = await UI.OpenFileDialog(jsonFilter);
                interaction.SetOutput(file);
            }).DisposeWith(disposables);

            ViewModel.ShowHideWindowInteraction.RegisterHandler(interaction =>
            {
                ShowHideWindow(interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            // Do not bridge upstream v2rayN snack messages into the Dirac shell.
            AppEvents.AppExitRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(_ => StorageUI())
              .DisposeWith(disposables);

            AppEvents.ShutdownRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(Shutdown)
              .DisposeWith(disposables);
        });

        Title = "Dirac Desktop";
        if (Utils.IsWindows() && !Design.IsDesignMode)
        {
            ThreadPool.RegisterWaitForSingleObject(Program.ProgramStarted, OnProgramStarted, null, -1, false);
            HotkeyManager.Instance.Init(_config, OnHotkeyHandler);
        }

        if (_config.UiItem.AutoHideStartup && Utils.IsWindows())
        {
            WindowState = WindowState.Minimized;
        }
    }

    // Keep the previously tested v2rayN/Xray engine intact. This first
    // dashboard is only another view of its managed state and commands.
    private void WireDiracDashboard()
    {
        diracHome.ConnectRequested += async (_, _) => await ToggleDiracConnectionAsync();
        diracHome.NetworkCheckRequested += async (_, _) => await CheckDiracNetworkAsync(force: true);
        diracHome.ImportFileRequested += async (_, _) =>
        {
            await ViewModel.ImportDiracProfileFileAsync();
            RefreshDiracDashboard();
        };
        diracHome.ImportClipboardRequested += async (_, _) =>
        {
            await ViewModel.ImportDiracProfileClipboardAsync();
            RefreshDiracDashboard();
        };
        diracHome.RussiaDirectRequested += async (_, _) => await SetDiracDashboardRouteAsync(true);
        diracHome.FullVpnRequested += async (_, _) => await SetDiracDashboardRouteAsync(false);
        diracHome.StableRequested += async (_, _) =>
        {
            await SelectDiracUpdateChannelAsync(beta: false);
            RefreshDiracDashboard();
        };
        diracHome.BetaRequested += async (_, _) =>
        {
            await SelectDiracUpdateChannelAsync(beta: true);
            RefreshDiracDashboard();
        };
        diracHome.CheckUpdateRequested += (_, _) => MenuDiracCheckUpdate_Click(diracHome, new RoutedEventArgs());
        _diracDashboardTimer.Tick += (_, _) => RefreshDiracDashboard();
    }

    private async Task ToggleDiracConnectionAsync()
    {
        if (_diracPowerBusy || ViewModel is null)
        {
            return;
        }

        _diracPowerBusy = true;
        try
        {
            var status = ViewModel.StatusBarViewModel;
            if (status.EnableTun || _config.TunModeItem.EnableTun)
            {
                status.EnableTun = false;
                RefreshDiracDashboard();
                return;
            }
            if (CoreManager.Instance.ActiveDiracRussiaDirect.HasValue)
            {
                NotifyDiracUpdate("Дождитесь завершения отключения VPN.", NotificationType.Warning);
                return;
            }

            var profile = await ConfigHandler.GetDefaultServer(_config);
            if (profile is null)
            {
                NotifyDiracUpdate("Сначала импортируйте профиль Dirac из файла или буфера.", NotificationType.Warning);
                return;
            }
            if (profile.ConfigType != EConfigType.Custom || profile.CoreType != ECoreType.Xray
                || !await DiracDohBootstrap.IsEligibleFileAsync(Utils.GetConfigPath(profile.Address)))
            {
                NotifyDiracUpdate(
                    "Нужен совместимый полный JSON-профиль Dirac, полученный от владельца.",
                    NotificationType.Warning);
                return;
            }

            if (DiracTunExclusivity.IsForeignTunActive(CoreManager.Instance.ActiveDiracRussiaDirect.HasValue))
            {
                NotifyDiracUpdate(
                    "Уже работает другая копия Dirac TUN. Отключите прежний VPN перед подключением нового.",
                    NotificationType.Warning);
                return;
            }

            if (Utils.IsWindows() && !Utils.IsAdministrator())
            {
                NotifyDiracUpdate("Для native TUN Dirac запросит запуск от администратора.");
            }

            _diracConnectStartedUtc = DateTime.UtcNow;
            status.EnableTun = true; // Original VM persists state and owns reload/elevation.
            RefreshDiracDashboard();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Dirac dashboard connection action failed: " + ex.GetType().Name);
            NotifyDiracUpdate("Не удалось переключить VPN. Проверьте журнал.", NotificationType.Error);
        }
        finally
        {
            _diracPowerBusy = false;
        }
    }

    private async Task SetDiracDashboardRouteAsync(bool russiaDirect)
    {
        if (ViewModel is null)
        {
            return;
        }
        var state = GetDiracDashboardState();
        if (state is EDiracDashboardState.Connecting or EDiracDashboardState.Disconnecting)
        {
            NotifyDiracUpdate("Дождитесь завершения переключения VPN.", NotificationType.Warning);
            return;
        }

        await ViewModel.SetDiracRouteModeAsync(russiaDirect);
        RefreshDiracDashboard();
    }

    private EDiracDashboardState GetDiracDashboardState()
    {
        var requested = _config.TunModeItem.EnableTun;
        var active = CoreManager.Instance.ActiveDiracRussiaDirect;
        var running = CoreManager.Instance.IsMainCoreRunning;

        if (requested && !active.HasValue && _diracConnectStartedUtc is null)
        {
            _diracConnectStartedUtc = DateTime.UtcNow;
        }
        if (!requested || active.HasValue)
        {
            _diracConnectStartedUtc = null;
        }

        return DiracDashboardState.Get(requested, running, active,
            _diracConnectStartedUtc.HasValue
                ? DateTime.UtcNow - _diracConnectStartedUtc.Value
                : TimeSpan.Zero);
    }

    private void RefreshDiracDashboard()
    {
        if (ViewModel is null || !Utils.IsWindows())
        {
            return;
        }

        var current = GetDiracDashboardState();
        var display = current switch
        {
            EDiracDashboardState.Connecting => DiracConnectionDisplay.Connecting,
            EDiracDashboardState.Connected => DiracConnectionDisplay.Connected,
            EDiracDashboardState.Disconnecting => DiracConnectionDisplay.Disconnecting,
            EDiracDashboardState.Unavailable => DiracConnectionDisplay.Unavailable,
            _ => DiracConnectionDisplay.Disconnected
        };

        diracHome.SetConnectionState(display);
        diracHome.SetRouting(
            _config.TunModeItem.DiracRussiaDirect,
            current is not EDiracDashboardState.Connecting and not EDiracDashboardState.Disconnecting,
            current == EDiracDashboardState.Connected);
        diracHome.SetUpdateChannel(_config.CheckUpdateItem.DiracBetaChannel, _diracUpdateBusy);
        diracHome.SetProfileCount(ViewModel.StatusBarViewModel.Servers.Count);
        RefreshDiracNetwork(current);
    }

    #region Event

    private void OnProgramStarted(object state, bool timeout)
    {
        Dispatcher.UIThread.Post(() =>
                ShowHideWindow(true),
            DispatcherPriority.Default);
    }

    private async Task DelegateSnackMsg(string content)
    {
        _manager?.Show(new Avalonia.Controls.Notifications.Notification(null, content, NotificationType.Information));
        await Task.CompletedTask;
    }

    private void OnHotkeyHandler(EGlobalHotkey e)
    {
        switch (e)
        {
            case EGlobalHotkey.ShowForm:
                Dispatcher.UIThread.Post(() => ShowHideWindow(null));
                break;

            case EGlobalHotkey.SystemProxyClear:
            case EGlobalHotkey.SystemProxySet:
            case EGlobalHotkey.SystemProxyUnchanged:
            case EGlobalHotkey.SystemProxyPac:
                AppEvents.SysProxyChangeRequested.Publish((ESysProxyType)((int)e - 1));
                break;
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_blCloseByUser)
        {
            return;
        }

        Logging.SaveLog("OnClosing -> " + e.CloseReason.ToString());

        switch (e.CloseReason)
        {
            case WindowCloseReason.OwnerWindowClosing or WindowCloseReason.WindowClosing:
                e.Cancel = true;
                ShowHideWindow(false);
                break;

            case WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown:
                await AppManager.Instance.AppExitAsync(false);
                break;
        }

        base.OnClosing(e);
    }

    private void RefreshDiracUpdateChannelMenu()
    {
        diracHome.SetUpdateChannel(_config.CheckUpdateItem.DiracBetaChannel, _diracUpdateBusy);
    }

    private async Task SelectDiracUpdateChannelAsync(bool beta)
    {
        if (_diracUpdateBusy)
        {
            RefreshDiracUpdateChannelMenu();
            return;
        }

        var previous = _config.CheckUpdateItem.DiracBetaChannel;
        _config.CheckUpdateItem.DiracBetaChannel = beta;
        if (await ConfigHandler.SaveConfig(_config) != 0)
        {
            _config.CheckUpdateItem.DiracBetaChannel = previous;
            NotifyDiracUpdate("Could not save Dirac app update channel.", NotificationType.Error);
        }

        RefreshDiracUpdateChannelMenu();
    }

    private void NotifyDiracUpdate(string message, NotificationType type = NotificationType.Information)
    {
        _manager?.Show(new Avalonia.Controls.Notifications.Notification("Dirac update", message, type));
    }

    private async void MenuDiracCheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (_diracUpdateBusy)
        {
            return;
        }

        _diracUpdateBusy = true;
        RefreshDiracUpdateChannelMenu();

        try
        {
            var beta = _config.CheckUpdateItem.DiracBetaChannel;
            var channel = beta ? "beta" : "stable";
            NotifyDiracUpdate($"Checking public GitHub Releases ({channel})...");
            var check = await _diracUpdates.CheckGitHubAsync(beta);
            if (!check.IsInstalled)
            {
                NotifyDiracUpdate(
                    "App updates require an installed Dirac build. Portable/development builds cannot self-update.");
                return;
            }

            if (check.Errors.Count > 0)
            {
                // A private repository and an unpublished release both
                // prevent anonymous friends from accessing GitHub Releases.
                // Do not put raw HTTP errors or tokens into UI notifications.
                NotifyDiracUpdate(
                    "GitHub Releases is unavailable for this channel. A public Dirac release may not be published yet.",
                    NotificationType.Warning);
                return;
            }

            if (check.Pending is not { } pending)
            {
                NotifyDiracUpdate($"Dirac is current on the {channel} channel.");
                return;
            }

            if (await UI.ShowYesNo(
                $"Dirac {pending.Version} ({channel}) is available on GitHub. Download this app update?") != ButtonResult.Yes)
            {
                return;
            }

            var reported = -1;
            await DiracAppUpdateService.DownloadAsync(pending, percent =>
            {
                var quarter = Math.Clamp(percent, 0, 100) / 25;
                if (quarter > reported)
                {
                    reported = quarter;
                    Dispatcher.UIThread.Post(() =>
                        NotifyDiracUpdate($"Downloading Dirac {pending.Version}: {Math.Clamp(percent, 0, 100)}%"));
                }
            });
            NotifyDiracUpdate($"Dirac {pending.Version} has been downloaded and verified by Velopack.");

            if (_config.TunModeItem.EnableTun && CoreManager.Instance.IsMainCoreRunning)
            {
                // The connected-TUN application-update sequence still needs
                // its own end-to-end test. Keep the downloaded package intact
                // and do not surprise the user by interrupting the VPN.
                NotifyDiracUpdate(
                    "Disconnect the active TUN first, then check Dirac updates again to install.",
                    NotificationType.Warning);
                return;
            }

            if (await UI.ShowYesNo(
                $"Install Dirac {pending.Version} now? The application will close, update and restart.") != ButtonResult.Yes)
            {
                return;
            }

            // Explicitly stop a leftover managed core before starting the
            // waiting updater; AppExitAsync also restores system DNS/proxy.
            await CoreManager.Instance.CoreStop();
            await DiracAppUpdateService.ApplyAndRestartAsync(pending);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("DiracGitHubReleasesUpdate", ex);
            NotifyDiracUpdate(
                $"Dirac application update failed ({ex.GetType().Name}). The existing installation was retained.",
                NotificationType.Error);
        }
        finally
        {
            _diracUpdateBusy = false;
            RefreshDiracUpdateChannelMenu();
        }
    }

    private async void MenuClose_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (await UI.ShowYesNo(ResUI.menuExitTips) != ButtonResult.Yes)
            {
                return;
            }

            _blCloseByUser = true;
            StorageUI();

            await AppManager.Instance.AppExitAsync(true);
        }
        catch
        {
            // Ignore
        }
    }

    private void Shutdown(bool obj)
    {
        if (obj is bool b && _blCloseByUser == false)
        {
            _blCloseByUser = b;
        }
        StorageUI();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            HotkeyManager.Instance.Dispose();
            desktop.Shutdown();
        }
    }

    #endregion Event

    #region UI

    public void ShowHideWindow(bool? blShow)
    {
        var bl = blShow ??
                    (Utils.IsLinux() || Utils.IsMacOS()
                    ? (!AppManager.Instance.ShowInTaskbar ^ (WindowState == WindowState.Minimized))
                    : !AppManager.Instance.ShowInTaskbar);
        if (bl)
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Focus();
        }
        else
        {
            if (Utils.IsLinux() && _config.UiItem.Hide2TrayWhenClose == false)
            {
                WindowState = WindowState.Minimized;
                return;
            }

            foreach (var ownedWindow in OwnedWindows)
            {
                ownedWindow.Close();
            }
            Hide();
        }

        AppManager.Instance.ShowInTaskbar = bl;
    }

    protected override void OnLoaded(object? sender, RoutedEventArgs e)
    {
        base.OnLoaded(sender, e);
        if (_config.UiItem.AutoHideStartup)
        {
            ShowHideWindow(false);
        }
        RefreshDiracDashboard();
        _diracDashboardTimer.Start();
    }

    private void StorageUI()
    {
        ConfigHandler.SaveWindowSizeItem(_config, GetType().Name, Width, Height);
    }

    #endregion UI
}
