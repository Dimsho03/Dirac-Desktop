using System.Security.Cryptography;
using ServiceLib.Common;
using Velopack;
using Velopack.Locators;

namespace v2rayN.Desktop.Services;

internal static class DiracInstalledUpdateSmoke
{
    private const string StartArgument = "--dirac-installed-update-smoke";
    private const string VerifyArgument = "--dirac-installed-update-verify";
    private const string Channel = "win-x64-stable";

    private static readonly Dictionary<string, string> ExpectedRuntimeHashes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine("bin", "xray", "xray.exe")] =
                "5B4DBCF2F8E2E1D9A7E1BB47C5EE06FC355E30F70DDCD0CEE1D37A662CFFE01D",
            [Path.Combine("bin", "xray", "wintun.dll")] =
                "E5DA8447DC2C320EDC0FC52FA01885C103DE8C118481F683643CACC3220DAFCE",
            [Path.Combine("bin", "geoip.dat")] =
                "3FF5C8723894A880B4AF93E1B0436C39226A98FBB64B6CD42C184588087241A7",
            [Path.Combine("bin", "geosite.dat")] =
                "9B03F2E7B978D524E437D49869569B74124B0D744A3721CB38CF7522188FBFE4",
        };

    public static bool TryHandle(string[] args)
    {
        if (args.Length == 0)
        {
            return false;
        }

        if (args[0].Equals(StartArgument, StringComparison.Ordinal))
        {
            if (args.Length != 3)
            {
                throw new ArgumentException($"{StartArgument} requires <feed-directory> <result-file>.");
            }

            RunStartAsync(args[1], args[2]).GetAwaiter().GetResult();
            return true;
        }

        if (args[0].Equals(VerifyArgument, StringComparison.Ordinal))
        {
            if (args.Length != 3)
            {
                throw new ArgumentException($"{VerifyArgument} requires <result-file> <expected-version>.");
            }

            RunVerify(args[1], args[2]);
            return true;
        }

        return false;
    }

    private static async Task RunStartAsync(string feedDirectory, string resultFile)
    {
        var resultPath = Path.GetFullPath(resultFile);
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        File.WriteAllText(resultPath, string.Empty);

        var manager = new UpdateManager(
            Path.GetFullPath(feedDirectory),
            new UpdateOptions { ExplicitChannel = Channel });

        Append(resultPath, $"INSTALLED={manager.IsInstalled}");
        Append(resultPath, $"CURRENT={manager.CurrentVersion}");
        if (!manager.IsInstalled || manager.CurrentVersion is null)
        {
            throw new InvalidOperationException("Velopack did not recognize the current Dirac process as installed.");
        }

        VerifyRuntime(Utils.StartupPath());
        var persistentMarker = Path.Combine(Utils.StartupPath(), "installed-update-smoke.marker");
        File.WriteAllText(persistentMarker, manager.CurrentVersion.ToString());

        var update = await manager.CheckForUpdatesAsync()
            ?? throw new InvalidOperationException("No newer Dirac release was found in the smoke feed.");

        var expectedVersion = update.TargetFullRelease.Version.ToString();
        Append(resultPath, $"TARGET={expectedVersion}");
        Append(resultPath, $"DELTA_COUNT={update.DeltasToTarget?.Length ?? 0}");

        await manager.DownloadUpdatesAsync(
            update,
            progress => Append(resultPath, $"DOWNLOAD={progress}"));

        Append(resultPath, "DOWNLOAD_COMPLETE=true");
        manager.WaitExitThenApplyUpdates(
            update.TargetFullRelease,
            silent: true,
            restart: true,
            restartArgs: [VerifyArgument, resultPath, expectedVersion]);

        Append(resultPath, "APPLY_SCHEDULED=true");
    }

    private static void RunVerify(string resultFile, string expectedVersion)
    {
        var resultPath = Path.GetFullPath(resultFile);
        var locator = VelopackLocator.IsCurrentSet ? VelopackLocator.Current : null;
        var actualVersion = locator?.CurrentlyInstalledVersion?.ToString() ?? string.Empty;
        Append(resultPath, $"RESTARTED_VERSION={actualVersion}");

        if (!actualVersion.Equals(expectedVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Installed version after restart is {actualVersion}, expected {expectedVersion}.");
        }

        var dataRoot = Utils.StartupPath();
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            DiracUpdateRuntime.DataFolderName);
        if (!Path.GetFullPath(dataRoot).Equals(Path.GetFullPath(expectedRoot), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Persistent data root mismatch: {dataRoot}");
        }

        VerifyRuntime(dataRoot);

        var persistentMarker = Path.Combine(dataRoot, "installed-update-smoke.marker");
        if (!File.Exists(persistentMarker) || string.IsNullOrWhiteSpace(File.ReadAllText(persistentMarker)))
        {
            throw new InvalidOperationException("Persistent data marker did not survive the application update.");
        }

        File.Delete(persistentMarker);
        Append(resultPath, "PERSISTENT_DATA_SURVIVED=true");
        Append(resultPath, "RUNTIME_HASHES_VERIFIED=true");
        Append(resultPath, "INSTALLED_UPDATE_SMOKE_PASS=true");
    }

    private static void VerifyRuntime(string dataRoot)
    {
        foreach (var item in ExpectedRuntimeHashes)
        {
            var path = Path.Combine(dataRoot, item.Key);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Installed Dirac runtime file is missing.", path);
            }

            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!actual.Equals(item.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Installed Dirac runtime SHA-256 mismatch: {item.Key}");
            }
        }

        var marker = Path.Combine(dataRoot, "bin", "xray", "dirac-core.sha256");
        if (!File.Exists(marker) ||
            !File.ReadAllText(marker).Trim().Equals(
                ExpectedRuntimeHashes[Path.Combine("bin", "xray", "xray.exe")],
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Installed Dirac Xray marker does not match the pinned core.");
        }
    }

    private static void Append(string path, string line)
    {
        File.AppendAllText(
            path,
            $"{DateTimeOffset.UtcNow:O} {line}{Environment.NewLine}");
    }
}