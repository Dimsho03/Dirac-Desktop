using Avalonia.Controls;
using Avalonia.Interactivity;

namespace v2rayN.Desktop.Views;

/// <summary>
/// First, deliberately small Dirac user-facing shell. The underlying MainWindow
/// and upstream v2rayN views remain available as the advanced workspace.
/// Connection state is supplied by CoreManager, never inferred from a button.
/// </summary>
public partial class DiracHomeView : UserControl
{
    public event EventHandler? ConnectRequested;
    public event EventHandler? ProfilesRequested;
    public event EventHandler? DiagnosticsRequested;
    public event EventHandler? AdvancedRequested;
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
        btnDiracProfiles.Click += (_, _) => ProfilesRequested?.Invoke(this, EventArgs.Empty);
        btnDiracDiagnostics.Click += (_, _) => DiagnosticsRequested?.Invoke(this, EventArgs.Empty);
        btnDiracAdvanced.Click += (_, _) => AdvancedRequested?.Invoke(this, EventArgs.Empty);
        btnDiracImportFile.Click += (_, _) => ImportFileRequested?.Invoke(this, EventArgs.Empty);
        btnDiracImportClipboard.Click += (_, _) => ImportClipboardRequested?.Invoke(this, EventArgs.Empty);
        btnDiracRussia.Click += (_, _) => RussiaDirectRequested?.Invoke(this, EventArgs.Empty);
        btnDiracFull.Click += (_, _) => FullVpnRequested?.Invoke(this, EventArgs.Empty);
        btnDiracStable.Click += (_, _) => StableRequested?.Invoke(this, EventArgs.Empty);
        btnDiracBeta.Click += (_, _) => BetaRequested?.Invoke(this, EventArgs.Empty);
        btnDiracCheckUpdate.Click += (_, _) => CheckUpdateRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetConnectionState(DiracConnectionDisplay state)
    {
        switch (state)
        {
            case DiracConnectionDisplay.Connected:
                txtDiracHeaderStatus.Text = "●  Подключено";
                txtDiracConnection.Text = "Подключено";
                txtDiracConnectionHint.Text = "Трафик TUN проходит через Dirac";
                txtDiracDns.Text = "Локальная защита включена";
                btnDiracConnect.Content = "⏻";
                btnDiracConnect.BorderBrush = Avalonia.Media.Brush.Parse("#A0FFD8");
                btnDiracConnect.Background = Avalonia.Media.Brush.Parse("#275B4B");
                ToolTip.SetTip(btnDiracConnect, "Отключить VPN");
                break;

            case DiracConnectionDisplay.Disconnecting:
                txtDiracHeaderStatus.Text = "◌  Отключение";
                txtDiracConnection.Text = "Отключение…";
                txtDiracConnectionHint.Text = "Останавливаем TUN и восстанавливаем обычную сеть";
                txtDiracDns.Text = "Возврат обычного DNS…";
                btnDiracConnect.Content = "⏻";
                btnDiracConnect.BorderBrush = Avalonia.Media.Brush.Parse("#E1B870");
                btnDiracConnect.Background = Avalonia.Media.Brush.Parse("#50422E");
                ToolTip.SetTip(btnDiracConnect, "Завершение работы VPN");
                break;

            case DiracConnectionDisplay.Connecting:
                txtDiracHeaderStatus.Text = "◌  Подключение";
                txtDiracConnection.Text = "Подключение…";
                txtDiracConnectionHint.Text = "Запуск Xray, TUN и защищённого DNS";
                txtDiracDns.Text = "Проверка состояния…";
                btnDiracConnect.Content = "⏻";
                btnDiracConnect.BorderBrush = Avalonia.Media.Brush.Parse("#E1B870");
                btnDiracConnect.Background = Avalonia.Media.Brush.Parse("#50422E");
                ToolTip.SetTip(btnDiracConnect, "Отменить подключение");
                break;

            case DiracConnectionDisplay.Unavailable:
                txtDiracHeaderStatus.Text = "●  Нет соединения";
                txtDiracConnection.Text = "Не удалось подключиться";
                txtDiracConnectionHint.Text = "Проверьте профиль или откройте журнал";
                txtDiracDns.Text = "Состояние проверяется";
                btnDiracConnect.Content = "⏻";
                btnDiracConnect.BorderBrush = Avalonia.Media.Brush.Parse("#DDA27F");
                btnDiracConnect.Background = Avalonia.Media.Brush.Parse("#4A342D");
                ToolTip.SetTip(btnDiracConnect, "Отключить попытку VPN");
                break;

            default:
                txtDiracHeaderStatus.Text = "●  Отключено";
                txtDiracConnection.Text = "Отключено";
                txtDiracConnectionHint.Text = "Выберите профиль и нажмите кнопку";
                txtDiracDns.Text = "Активируется вместе с VPN";
                btnDiracConnect.Content = "⏻";
                btnDiracConnect.BorderBrush = Avalonia.Media.Brush.Parse("#62DAB2");
                btnDiracConnect.Background = Avalonia.Media.Brush.Parse("#20483F");
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
        txtDiracRoutingHint.Text = !canChange
            ? "Дождитесь завершения подключения."
            : isActive
                ? "Режим применяется к текущему подключению."
                : "Настройка сохранится для следующего подключения.";
    }

    public void SetUpdateChannel(bool beta, bool busy)
    {
        btnDiracStable.Classes.Set("selected", !beta);
        btnDiracBeta.Classes.Set("selected", beta);
        btnDiracStable.IsEnabled = !busy;
        btnDiracBeta.IsEnabled = !busy;
        btnDiracCheckUpdate.IsEnabled = !busy;
    }

    public void SetProfileCount(int count)
    {
        txtDiracProfileCount.Text = count switch
        {
            0 => "Нет профилей",
            1 => "1 профиль",
            _ => $"{count} профилей"
        };
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
