using Avalonia.Controls.Notifications;
using DialogHostAvalonia;
using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Manager;
using v2rayN.Desktop.Services;

namespace v2rayN.Desktop.Views;

public partial class MainWindow : WindowBase<MainWindowViewModel>
{
    private static Config _config;
    private readonly SingleReplaceableDisposable _layoutBindingsDisposable = new();
    private readonly WindowNotificationManager? _manager;
    private CheckUpdateView? _checkUpdateView;
    private BackupAndRestoreView? _backupAndRestoreView;
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
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this)) { MaxItems = 3, Position = NotificationPosition.TopRight };

        KeyDown += MainWindow_KeyDown;
        menuSettingsSetUWP.Click += MenuSettingsSetUWP_Click;
        menuPromotion.Click += MenuPromotion_Click;
        menuCheckUpdate.Click += MenuCheckUpdate_Click;
        menuDiracCheckUpdate.Click += MenuDiracCheckUpdate_Click;
        menuDiracStableChannel.Click += MenuDiracStableChannel_Click;
        menuDiracBetaChannel.Click += MenuDiracBetaChannel_Click;
        btnNewUpdate.Click += MenuDiracCheckUpdate_Click;
        RefreshDiracUpdateChannelMenu();
        WireDiracDashboard();
        menuBackupAndRestore.Click += MenuBackupAndRestore_Click;
        menuClose.Click += MenuClose_Click;

        conTheme.Content ??= new ThemeSettingView();

        this.WhenActivated(disposables =>
        {
            //servers
            this.BindCommand(ViewModel, vm => vm.AddVmessServerCmd, v => v.menuAddVmessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddVlessServerCmd, v => v.menuAddVlessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddShadowsocksServerCmd, v => v.menuAddShadowsocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSocksServerCmd, v => v.menuAddSocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHttpServerCmd, v => v.menuAddHttpServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTrojanServerCmd, v => v.menuAddTrojanServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHysteria2ServerCmd, v => v.menuAddHysteria2Server).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTuicServerCmd, v => v.menuAddTuicServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddWireguardServerCmd, v => v.menuAddWireguardServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddAnytlsServerCmd, v => v.menuAddAnytlsServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddNaiveServerCmd, v => v.menuAddNaiveServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomServerCmd, v => v.menuAddCustomServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomOutboundServerCmd, v => v.menuAddCustomOutboundServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddPolicyGroupServerCmd, v => v.menuAddPolicyGroupServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddProxyChainServerCmd, v => v.menuAddProxyChainServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ImportDiracProfileFileCmd, v => v.menuImportDiracProfileFile).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ImportDiracProfileClipboardCmd, v => v.menuImportDiracProfileClipboard).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaClipboardCmd, v => v.menuAddServerViaClipboard).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaScanCmd, v => v.menuAddServerViaScan).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaImageCmd, v => v.menuAddServerViaImage).DisposeWith(disposables);

            //sub
            this.BindCommand(ViewModel, vm => vm.SubSettingCmd, v => v.menuSubSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateCmd, v => v.menuSubUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateViaProxyCmd, v => v.menuSubUpdateViaProxy).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateCmd, v => v.menuSubGroupUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateViaProxyCmd, v => v.menuSubGroupUpdateViaProxy).DisposeWith(disposables);

            //setting
            this.BindCommand(ViewModel, vm => vm.OptionSettingCmd, v => v.menuOptionSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingSettingCmd, v => v.menuRoutingSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SetDiracRussiaDirectCmd, v => v.menuDiracRussiaDirect).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SetDiracFullVpnCmd, v => v.menuDiracFullVpn).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DNSSettingCmd, v => v.menuDNSSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.FullConfigTemplateCmd, v => v.menuFullConfigTemplate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GlobalHotkeySettingCmd, v => v.menuGlobalHotkeySetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RebootAsAdminCmd, v => v.menuRebootAsAdmin).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ClearServerStatisticsCmd, v => v.menuClearServerStatistics).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OpenTheFileLocationCmd, v => v.menuOpenTheFileLocation).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetDefaultCmd, v => v.menuRegionalPresetsDefault).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetRussiaCmd, v => v.menuRegionalPresetsRussia).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetIranCmd, v => v.menuRegionalPresetsIran).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.ReloadCmd, v => v.menuReload).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlReloadEnabled, v => v.menuReload.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlNewUpdate, v => v.btnNewUpdate.IsVisible).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.StatusBarViewModel, v => v.contentStatusBarView.Content).DisposeWith(disposables);

            _layoutBindingsDisposable.DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.MainGirdOrientation)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(UpdateLayout)
                .DisposeWith(disposables);

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

            ViewModel.ScanScreenInteraction.RegisterHandler(async interaction =>
            {
                ShowHideWindow(false);
                await Task.Delay(200);
                var result = QRCodeAvaloniaUtils.CaptureScreen();
                ShowHideWindow(true);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.BrowseImageFileInteraction.RegisterHandler(async interaction =>
            {
                var result = await UI.OpenFileDialog(null);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.ShowHideWindowInteraction.RegisterHandler(interaction =>
            {
                ShowHideWindow(interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            AppEvents.SendSnackMsgRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(async content => await DelegateSnackMsg(content))
              .DisposeWith(disposables);

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

        if (Utils.IsWindows())
        {
            Title = $"Dirac Desktop · {Utils.GetVersion()} - {(Utils.IsAdministrator() ? ResUI.RunAsAdmin : ResUI.NotRunAsAdmin)}";

            if (!Design.IsDesignMode)
            {
                ThreadPool.RegisterWaitForSingleObject(Program.ProgramStarted, OnProgramStarted, null, -1, false);
                HotkeyManager.Instance.Init(_config, OnHotkeyHandler);
            }
        }
        else
        {
            Title = $"Dirac Desktop · {Utils.GetVersion()}";
            menuAddServerViaScan.IsVisible = false;
        }

        if (_config.UiItem.AutoHideStartup && Utils.IsWindows())
        {
            WindowState = WindowState.Minimized;
        }

        AddHelpMenuItem();
    }

    // Keep the previously tested v2rayN/Xray engine intact. This first
    // dashboard is only another view of its managed state and commands.
    private void WireDiracDashboard()
    {
        diracHome.ConnectRequested += async (_, _) => await ToggleDiracConnectionAsync();
        diracHome.ProfilesRequested += (_, _) => ShowDiracAdvancedWorkspace("profiles");
        diracHome.DiagnosticsRequested += (_, _) => ShowDiracAdvancedWorkspace("logs");
        diracHome.AdvancedRequested += (_, _) => ShowDiracAdvancedWorkspace("advanced");
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
        btnBackDirac.Click += (_, _) =>
        {
            legacyWorkspace.IsVisible = false;
            diracHome.IsVisible = true;
            RefreshDiracDashboard();
        };
        _diracDashboardTimer.Tick += (_, _) => RefreshDiracDashboard();
    }

    private void ShowDiracAdvancedWorkspace(string section)
    {
        diracHome.IsVisible = false;
        legacyWorkspace.IsVisible = true;

        if (section == "logs")
        {
            switch (_config.UiItem.MainGirdOrientation)
            {
                case EGirdOrientation.Horizontal: tabMain.SelectedIndex = 0; break;
                case EGirdOrientation.Vertical: tabMain1.SelectedIndex = 0; break;
                default: tabMain2.SelectedIndex = 1; break;
            }
        }
        else if (section == "profiles" && _config.UiItem.MainGirdOrientation == EGirdOrientation.Tab)
        {
            tabMain2.SelectedIndex = 0;
        }
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
        diracHome.SetProfileCount(ViewModel.ProfilesViewModel.ProfileItems.Count);
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

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
        {
            switch (e.Key)
            {
                case Key.V:
                    await AddServerViaClipboardAsync();
                    break;

                case Key.S:
                    await ScanScreenTaskAsync();
                    break;
            }
        }
        else
        {
            if (e.Key == Key.F5)
            {
                ViewModel?.Reload();
            }
        }
    }

    private void MenuPromotion_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart($"{Utils.Base64Decode(Global.PromotionUrl)}?t={DateTime.Now.Ticks}");
    }

    private void MenuSettingsSetUWP_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart(Utils.GetBinPath("EnableLoopback.exe"));
    }

    public async Task AddServerViaClipboardAsync()
    {
        var clipboardData = await AvaUtils.GetClipboardData(this);
        if (clipboardData.IsNotEmpty() && ViewModel != null)
        {
            await ViewModel.AddServerViaClipboardAsync(clipboardData);
        }
    }

    public async Task ScanScreenTaskAsync()
    {
        ShowHideWindow(false);

        await Task.Delay(200);

        var bytes = QRCodeAvaloniaUtils.CaptureScreen();
        if (bytes != null && ViewModel != null)
        {
            await ViewModel.ScanScreenResult(bytes);
        }

        ShowHideWindow(true);
    }

    private void RefreshDiracUpdateChannelMenu()
    {
        var beta = _config.CheckUpdateItem.DiracBetaChannel;
        menuDiracStableChannel.IsChecked = !beta;
        menuDiracBetaChannel.IsChecked = beta;
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

    private async void MenuDiracStableChannel_Click(object? sender, RoutedEventArgs e)
    {
        await SelectDiracUpdateChannelAsync(beta: false);
    }

    private async void MenuDiracBetaChannel_Click(object? sender, RoutedEventArgs e)
    {
        await SelectDiracUpdateChannelAsync(beta: true);
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
        menuDiracCheckUpdate.IsEnabled = false;
        menuDiracStableChannel.IsEnabled = false;
        menuDiracBetaChannel.IsEnabled = false;

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
            menuDiracCheckUpdate.IsEnabled = true;
            menuDiracStableChannel.IsEnabled = true;
            menuDiracBetaChannel.IsEnabled = true;
        }
    }

    private void MenuCheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        _checkUpdateView ??= new CheckUpdateView();
        _checkUpdateView.ViewModel = ViewModel?.CheckUpdateViewModel;
        DialogHost.Show(_checkUpdateView);

        AppEvents.HasUpdateNotified.Publish(false);
    }

    private void MenuBackupAndRestore_Click(object? sender, RoutedEventArgs e)
    {
        _backupAndRestoreView ??= new BackupAndRestoreView();
        _backupAndRestoreView.ViewModel = ViewModel?.BackupAndRestoreViewModel;
        DialogHost.Show(_backupAndRestoreView);
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
        RestoreUI();
        RefreshDiracDashboard();
        _diracDashboardTimer.Start();
    }

    private void RestoreUI()
    {
        if (_config.UiItem.MainGirdHeight1 > 0 && _config.UiItem.MainGirdHeight2 > 0)
        {
            if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
            {
                gridMain.ColumnDefinitions[0].Width = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain.ColumnDefinitions[2].Width = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
            else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical)
            {
                gridMain1.RowDefinitions[0].Height = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain1.RowDefinitions[2].Height = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
        }
    }

    private void StorageUI()
    {
        ConfigHandler.SaveWindowSizeItem(_config, GetType().Name, Width, Height);

        if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
        {
            ConfigHandler.SaveMainGirdHeight(_config, gridMain.ColumnDefinitions[0].ActualWidth, gridMain.ColumnDefinitions[2].ActualWidth);
        }
        else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical)
        {
            ConfigHandler.SaveMainGirdHeight(_config, gridMain1.RowDefinitions[0].ActualHeight, gridMain1.RowDefinitions[2].ActualHeight);
        }
    }

    private void UpdateLayout(EGirdOrientation orientation)
    {
        var currentLayoutDisposables = new MultipleDisposable();
        _layoutBindingsDisposable.Create(currentLayoutDisposables);

        gridMain.IsVisible = orientation == EGirdOrientation.Horizontal;
        gridMain1.IsVisible = orientation == EGirdOrientation.Vertical;
        gridMain2.IsVisible = orientation == EGirdOrientation.Tab;

        switch (orientation)
        {
            case EGirdOrientation.Horizontal:
                this.OneWayBind(ViewModel, vm => vm.ProfilesViewModel, v => v.tabProfiles.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.MsgViewModel, v => v.tabMsgView.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashProxiesViewModel, v => v.tabClashProxies.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashConnectionsViewModel, v => v.tabClashConnections.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabMsgView.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections.IsVisible).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;

            case EGirdOrientation.Vertical:
                this.OneWayBind(ViewModel, vm => vm.ProfilesViewModel, v => v.tabProfiles1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.MsgViewModel, v => v.tabMsgView1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashProxiesViewModel, v => v.tabClashProxies1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashConnectionsViewModel, v => v.tabClashConnections1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabMsgView1.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies1.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections1.IsVisible).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain1.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;

            case EGirdOrientation.Tab:
            default:
                this.OneWayBind(ViewModel, vm => vm.ProfilesViewModel, v => v.tabProfiles2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.MsgViewModel, v => v.tabMsgView2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashProxiesViewModel, v => v.tabClashProxies2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashConnectionsViewModel, v => v.tabClashConnections2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies2.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections2.IsVisible).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain2.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;
        }

        RestoreUI();
    }

    private void AddHelpMenuItem()
    {
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo();
        foreach (var it in coreInfo
            .Where(t => t.CoreType is not ECoreType.v2fly
                        and not ECoreType.hysteria))
        {
            var item = new MenuItem()
            {
                Tag = it.Url?.Replace(@"/releases", ""),
                Header = string.Format(ResUI.menuWebsiteItem, it.CoreType.ToString().Replace("_", " ")).UpperFirstChar()
            };
            item.Click += MenuItem_Click;
            menuHelp.Items.Add(item);
        }
    }

    private void MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            ProcUtils.ProcessStart(item.Tag?.ToString());
        }
    }

    #endregion UI
}
