using ServiceLib.Manager;
using Velopack;
using Velopack.Sources;

namespace v2rayN.Desktop.Services;

internal enum DiracUpdateSourceKind
{
    Https,
    GitHub,
}

internal sealed record DiracUpdateEndpoint(
    string Name,
    DiracUpdateSourceKind Kind,
    string Location,
    bool IncludePrereleases = false);

internal sealed record DiracPendingAppUpdate(
    UpdateManager Manager,
    UpdateInfo Update,
    DiracUpdateEndpoint Endpoint)
{
    public string Version => Update.TargetFullRelease.Version.ToString();
}

internal sealed record DiracUpdateCheckResult(
    DiracPendingAppUpdate? Pending,
    IReadOnlyList<string> Errors,
    bool IsInstalled)
{
    public bool HasUpdate => Pending is not null;
}

internal sealed class DiracAppUpdateService
{
    public async Task<DiracUpdateCheckResult> CheckAsync(
        IReadOnlyList<DiracUpdateEndpoint> endpoints,
        string? explicitChannel = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        if (endpoints.Count == 0)
        {
            return new DiracUpdateCheckResult(
                null,
                ["No application update endpoints are configured."],
                false);
        }

        var errors = new List<string>();

        foreach (var endpoint in endpoints)
        {
            try
            {
                var manager = CreateManager(endpoint, explicitChannel);
                if (!manager.IsInstalled)
                {
                    return new DiracUpdateCheckResult(null, errors, false);
                }

                var update = await manager.CheckForUpdatesAsync();
                return new DiracUpdateCheckResult(
                    update is null ? null : new DiracPendingAppUpdate(manager, update, endpoint),
                    errors,
                    true);
            }
            catch (Exception ex)
            {
                errors.Add($"{endpoint.Name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return new DiracUpdateCheckResult(null, errors, true);
    }

    public static Task DownloadAsync(
        DiracPendingAppUpdate pending,
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pending);
        return pending.Manager.DownloadUpdatesAsync(pending.Update, progress, cancellationToken);
    }

    public static async Task ApplyAndRestartAsync(DiracPendingAppUpdate pending)
    {
        ArgumentNullException.ThrowIfNull(pending);

        pending.Manager.WaitExitThenApplyUpdates(
            pending.Update.TargetFullRelease,
            silent: false,
            restart: true);

        await AppManager.Instance.AppExitAsync(true);
    }

    private static UpdateManager CreateManager(
        DiracUpdateEndpoint endpoint,
        string? explicitChannel)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Location))
        {
            throw new ArgumentException("Update endpoint location is empty.", nameof(endpoint));
        }

        var options = string.IsNullOrWhiteSpace(explicitChannel)
            ? null
            : new UpdateOptions { ExplicitChannel = explicitChannel };

        return endpoint.Kind switch
        {
            DiracUpdateSourceKind.Https =>
                new UpdateManager(new SimpleWebSource(endpoint.Location), options),

            DiracUpdateSourceKind.GitHub =>
                new UpdateManager(
                    new GithubSource(
                        endpoint.Location,
                        accessToken: null,
                        prerelease: endpoint.IncludePrereleases),
                    options),

            _ => throw new ArgumentOutOfRangeException(nameof(endpoint.Kind)),
        };
    }
}