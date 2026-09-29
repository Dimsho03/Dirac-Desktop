using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;

namespace v2rayN.Desktop.Views;

public partial class MainWindow
{
    private const string DiracTunName = "dirac_single_tun";
    private long? _diracLastReceivedBytes;
    private long? _diracLastSentBytes;
    private DateTime _diracLastTrafficSampleUtc;
    private DateTime _diracNextNetworkProbeUtc;
    private bool _diracNetworkProbeBusy;
    private CancellationTokenSource? _diracNetworkProbeCancellation;

    private void RefreshDiracNetwork(EDiracDashboardState state)
    {
        if (state != EDiracDashboardState.Connected)
        {
            _diracNetworkProbeCancellation?.Cancel();
            _diracNextNetworkProbeUtc = DateTime.MinValue;
            _diracLastReceivedBytes = null;
            _diracLastSentBytes = null;
            diracHome.SetTraffic(null, null);
            diracHome.SetNetworkHealth(DiracNetworkHealth.Inactive);
            return;
        }

        SampleDiracTunTraffic();
        if (!_diracNetworkProbeBusy && DateTime.UtcNow >= _diracNextNetworkProbeUtc)
        {
            _ = CheckDiracNetworkAsync(force: false);
        }
    }

    private void SampleDiracTunTraffic()
    {
        try
        {
            var tun = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(item => item.Name.Equals(DiracTunName, StringComparison.OrdinalIgnoreCase)
                    && item.OperationalStatus == OperationalStatus.Up);
            if (tun is null)
            {
                ResetDiracTrafficSample();
                return;
            }

            var counters = tun.GetIPv4Statistics();
            var now = DateTime.UtcNow;
            if (_diracLastReceivedBytes.HasValue && _diracLastSentBytes.HasValue)
            {
                var seconds = (now - _diracLastTrafficSampleUtc).TotalSeconds;
                if (seconds > 0 && counters.BytesReceived >= _diracLastReceivedBytes.Value
                    && counters.BytesSent >= _diracLastSentBytes.Value)
                {
                    var down = (long)((counters.BytesReceived - _diracLastReceivedBytes.Value) / seconds);
                    var up = (long)((counters.BytesSent - _diracLastSentBytes.Value) / seconds);
                    diracHome.SetTraffic(down, up);
                }
                else
                {
                    diracHome.SetTraffic(null, null);
                }
            }

            _diracLastReceivedBytes = counters.BytesReceived;
            _diracLastSentBytes = counters.BytesSent;
            _diracLastTrafficSampleUtc = now;
        }
        catch (Exception)
        {
            ResetDiracTrafficSample();
        }
    }

    private void ResetDiracTrafficSample()
    {
        _diracLastReceivedBytes = null;
        _diracLastSentBytes = null;
        diracHome.SetTraffic(null, null);
    }

    private async Task CheckDiracNetworkAsync(bool force)
    {
        if (_diracNetworkProbeBusy || (!force && DateTime.UtcNow < _diracNextNetworkProbeUtc)
            || GetDiracDashboardState() != EDiracDashboardState.Connected)
        {
            return;
        }

        _diracNetworkProbeBusy = true;
        _diracNextNetworkProbeUtc = DateTime.UtcNow.AddSeconds(15);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _diracNetworkProbeCancellation = cancellation;
        diracHome.SetNetworkHealth(DiracNetworkHealth.Checking);

        try
        {
            var address = _config.SpeedTestItem.SpeedPingTestUrl;
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                diracHome.SetNetworkHealth(DiracNetworkHealth.Unavailable);
                return;
            }

            using var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(3)
            };
            using var client = new HttpClient(handler);
            var watch = Stopwatch.StartNew();
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead,
                cancellation.Token);
            watch.Stop();

            if (GetDiracDashboardState() == EDiracDashboardState.Connected)
            {
                diracHome.SetNetworkHealth(response.IsSuccessStatusCode
                    ? DiracNetworkHealth.Available : DiracNetworkHealth.Unavailable,
                    response.IsSuccessStatusCode ? (int)watch.ElapsedMilliseconds : null);
            }
        }
        catch (Exception)
        {
            if (GetDiracDashboardState() == EDiracDashboardState.Connected)
            {
                diracHome.SetNetworkHealth(DiracNetworkHealth.Unavailable);
            }
        }
        finally
        {
            if (ReferenceEquals(_diracNetworkProbeCancellation, cancellation))
            {
                _diracNetworkProbeCancellation = null;
            }
            _diracNetworkProbeBusy = false;
        }
    }
}
