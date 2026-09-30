using Avalonia.Controls;
using Avalonia.Media;

namespace v2rayN.Desktop.Views;

/// <summary>
/// Compact native Dirac shell. Presentation only: the owning MainWindow still
/// uses its original CoreManager and view model for connection/routing/update.
/// </summary>
public partial class DiracHomeView : UserControl
{
    private bool _canSelectProfile;
    private int _profileCount;
    private DiracConnectionDisplay _connectionState;
    public event EventHandler? ConnectRequested;
    public event EventHandler? NetworkCheckRequested;
    public event EventHandler? ImportFileRequested;
    public event EventHandler? ImportClipboardRequested;
    public event EventHandler? RussiaDirectRequested;
    public event EventHandler? FullVpnRequested;
    public event EventHandler? StableRequested;
    public event EventHandler? BetaRequested;
    public event EventHandler? CheckUpdateRequested;

    public DiracHomeView()
    {
        InitializeComponent();
        btnDiracConnect.Click += (_, _) => ConnectRequested?.Invoke(this, EventArgs.Empty);
        btnDiracCheckNetwork.Click += (_, _) => NetworkCheckRequested?.Invoke(this, EventArgs.Empty);
        btnDiracOverview.Click += (_, _) => ShowSection("home");
        btnDiracRoutingNav.Click += (_, _) => ShowSection("routing");
        btnDiracUpdatesNav.Click += (_, _) => ShowSection("updates");
        btnDiracSettingsNav.Click += (_, _) => ShowSection("settings");
        btnDiracImportFile.Click += (_, _) => ImportFileRequested?.Invoke(this, EventArgs.Empty);
        btnDiracImportClipboard.Click += (_, _) => ImportClipboardRequested?.Invoke(this, EventArgs.Empty);
        btnDiracRussia.Click += (_, _) => RussiaDirectRequested?.Invoke(this, EventArgs.Empty);
        btnDiracFull.Click += (_, _) => FullVpnRequested?.Invoke(this, EventArgs.Empty);
        btnDiracStable.Click += (_, _) => StableRequested?.Invoke(this, EventArgs.Empty);
        btnDiracBeta.Click += (_, _) => BetaRequested?.Invoke(this, EventArgs.Empty);
        btnDiracCheckUpdate.Click += (_, _) => CheckUpdateRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetPreviewProfile(string label)
    {
        var option = new ComboBoxItem { Content = label };
        cmbDiracProfile.DisplayMemberBinding = null;
        cmbDiracProfile.ItemsSource = new[] { option };
        cmbDiracProfile.SelectedIndex = 0;
        SetProfileCount(1);
    }

    public void ShowSection(string section)
    {
        panelDiracHome.IsVisible = section == "home";
        panelDiracRouting.IsVisible = section == "routing";
        panelDiracUpdates.IsVisible = section == "updates";
        panelDiracSettings.IsVisible = section == "settings";
        btnDiracOverview.Classes.Set("selected", section == "home");
        btnDiracRoutingNav.Classes.Set("selected", section == "routing");
        btnDiracUpdatesNav.Classes.Set("selected", section == "updates");
        btnDiracSettingsNav.Classes.Set("selected", section == "settings");
    }

    public void SetConnectionState(DiracConnectionDisplay state)
    {
        _connectionState = state;
        var connected = state == DiracConnectionDisplay.Connected;
        btnDiracConnect.Classes.Set("connected", connected);
        diracConnectHalo.Classes.Set("connected", connected);
        _canSelectProfile = state == DiracConnectionDisplay.Disconnected;
        UpdateProfilePicker();
        switch (state)
        {
            case DiracConnectionDisplay.Connected:
                txtDiracConnection.Text = "Подключено";
                txtDiracConnectionHint.Text = "Соединение установлено";
                txtDiracAction.Text = "Отключить";
                ToolTip.SetTip(btnDiracConnect, "Отключить VPN");
                break;

            case DiracConnectionDisplay.Disconnecting:
                txtDiracConnection.Text = "Отключение…";
                txtDiracConnectionHint.Text = "Останавливаем TUN и восстанавливаем сеть";
                txtDiracAction.Text = "Отключение";
                ToolTip.SetTip(btnDiracConnect, "Завершение работы VPN");
                break;

            case DiracConnectionDisplay.Connecting:
                txtDiracConnection.Text = "Подключение…";
                txtDiracConnectionHint.Text = "Запуск Xray, TUN и DNS";
                txtDiracAction.Text = "Подключение";
                ToolTip.SetTip(btnDiracConnect, "Отменить подключение");
                break;

            case DiracConnectionDisplay.Unavailable:
                txtDiracConnection.Text = "Нет соединения";
                txtDiracConnectionHint.Text = "Проверьте профиль или откройте журнал";
                txtDiracAction.Text = "Повторить";
                ToolTip.SetTip(btnDiracConnect, "Попробовать подключиться");
                break;

            default:
                txtDiracConnection.Text = "Не подключено";
                txtDiracConnectionHint.Text = _profileCount == 0
                    ? "Добавьте профиль для подключения"
                    : "Выберите профиль для подключения";
                txtDiracAction.Text = "Подключить";
                ToolTip.SetTip(btnDiracConnect, "Подключить VPN");
                break;
        }
    }

    public void SetRouting(bool russiaDirect, bool canChange, bool isActive)
    {
        btnDiracRussia.Classes.Set("selected", russiaDirect);
        btnDiracFull.Classes.Set("selected", !russiaDirect);
        btnDiracRussia.IsEnabled = canChange;
        btnDiracFull.IsEnabled = canChange;
        if (isActive)
        {
            txtDiracConnectionHint.Text = russiaDirect ? "Режим: Россия напрямую" : "Режим: Полный VPN";
        }
        txtDiracRoutingHint.Text = !canChange
            ? "Дождитесь завершения подключения."
            : isActive
                ? "Режим применяется к текущему подключению."
                : "Выбранный режим сохранится для следующего подключения.";
    }

    public void SetUpdateChannel(bool beta, bool busy)
    {
        btnDiracStable.Classes.Set("selected", !beta);
        btnDiracBeta.Classes.Set("selected", beta);
        btnDiracStable.IsEnabled = !busy;
        btnDiracBeta.IsEnabled = !busy;
        btnDiracCheckUpdate.IsEnabled = !busy;
    }

    public void SetNetworkHealth(DiracNetworkHealth health, int? latencyMs = null)
    {
        txtDiracNetwork.Text = health switch
        {
            DiracNetworkHealth.Checking => "Проверка сети…",
            DiracNetworkHealth.Available => "Сеть доступна",
            DiracNetworkHealth.Unavailable => "Нет ответа",
            _ => "Не проверяется"
        };
        txtDiracLatency.Text = health == DiracNetworkHealth.Available && latencyMs.HasValue
            ? $"{latencyMs.Value} мс" : "—";
        var brush = new SolidColorBrush(Color.Parse(health switch
        {
            DiracNetworkHealth.Available => "#91B9A0",
            DiracNetworkHealth.Unavailable => "#C78C8C",
            DiracNetworkHealth.Checking => "#B6AE91",
            _ => "#9CA6A7"
        }));
        iconDiracNetwork.Foreground = brush;
        txtDiracNetwork.Foreground = brush;
        txtDiracCheckAge.Text = health is DiracNetworkHealth.Available or DiracNetworkHealth.Unavailable
            ? "сейчас" : "";
        btnDiracCheckNetwork.IsEnabled = health is DiracNetworkHealth.Available or DiracNetworkHealth.Unavailable;
    }

    public void SetNetworkCheckAge(TimeSpan age)
    {
        var seconds = Math.Max(0, (int)age.TotalSeconds);
        txtDiracCheckAge.Text = seconds < 5 ? "сейчас"
            : seconds < 60 ? $"{seconds} с назад"
            : $"{seconds / 60} мин назад";
    }

    public void SetTraffic(long? downloadBytesPerSecond, long? uploadBytesPerSecond)
    {
        txtDiracDownload.Text = FormatSpeed(downloadBytesPerSecond);
        txtDiracUpload.Text = FormatSpeed(uploadBytesPerSecond);
    }

    private static string FormatSpeed(long? bytesPerSecond)
    {
        if (!bytesPerSecond.HasValue)
        {
            return "—";
        }
        var value = Math.Max(0, bytesPerSecond.Value);
        if (value < 1024)
        {
            return $"{value} Б/с";
        }
        return value < 1024 * 1024
            ? $"{value / 1024d:0.#} КБ/с"
            : $"{value / (1024d * 1024):0.#} МБ/с";
    }

    public void SetProfileCount(int count)
    {
        _profileCount = count;
        txtDiracProfileEmpty.IsVisible = count == 0;
        txtDiracProfileCount.Text = count switch
        {
            0 => "Нет профилей",
            1 => "1 профиль",
            _ => $"{count} профилей"
        };
        if (_connectionState == DiracConnectionDisplay.Disconnected)
        {
            txtDiracConnectionHint.Text = count == 0
                ? "Добавьте профиль для подключения"
                : "Выберите профиль для подключения";
        }
        UpdateProfilePicker();
    }

    private void UpdateProfilePicker()
    {
        var hasProfiles = _profileCount > 0;
        cmbDiracProfile.IsEnabled = _canSelectProfile && hasProfiles;
        ToolTip.SetTip(cmbDiracProfile, !hasProfiles
            ? "Добавьте профиль из файла или буфера"
            : _canSelectProfile
                ? "Выберите профиль для подключения"
                : "Чтобы сменить профиль, отключите VPN");
    }
}

public enum DiracConnectionDisplay
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Unavailable
}
public enum DiracNetworkHealth
{
    Inactive,
    Checking,
    Available,
    Unavailable
}
