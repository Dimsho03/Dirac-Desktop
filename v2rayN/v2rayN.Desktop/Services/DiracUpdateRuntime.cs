using ServiceLib;
using Velopack.Locators;

namespace v2rayN.Desktop.Services;

internal static class DiracUpdateRuntime
{
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
            "Dirac");

        Environment.SetEnvironmentVariable(
            "DIRAC_DATA_ROOT",
            dataRoot,
            EnvironmentVariableTarget.Process);
    }
}