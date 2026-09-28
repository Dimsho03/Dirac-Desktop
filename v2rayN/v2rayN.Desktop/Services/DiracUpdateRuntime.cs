using ServiceLib;
using Velopack.Locators;

namespace v2rayN.Desktop.Services;

internal static class DiracUpdateRuntime
{
    public const string DataFolderName = "Dirac";

    public static void ConfigurePersistentDataPath()
    {
        Environment.SetEnvironmentVariable("DIRAC_MANAGED_APP_UPDATE", "1", EnvironmentVariableTarget.Process);

        if (!OperatingSystem.IsWindows() || !VelopackLocator.IsCurrentSet)
        {
            return;
        }

        var locator = VelopackLocator.Current;
        if (locator.CurrentlyInstalledVersion is null || locator.IsPortable)
        {
            return;
        }

        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            DataFolderName);

        Environment.SetEnvironmentVariable(
            Global.LocalAppData,
            "1",
            EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable(
            "DIRAC_DATA_ROOT",
            dataRoot,
            EnvironmentVariableTarget.Process);

        SyncReleaseRuntime(AppContext.BaseDirectory, dataRoot);
    }

    internal static void SyncReleaseRuntime(string releaseRoot, string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        var source = Path.Combine(Path.GetFullPath(releaseRoot), "bin");
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Dirac installed release runtime is missing: {source}");
        }

        var destination = Path.Combine(Path.GetFullPath(dataRoot), "bin");
        CopyDirectory(source, destination);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(
                destination,
                Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }
}