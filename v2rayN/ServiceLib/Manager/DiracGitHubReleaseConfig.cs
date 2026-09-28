namespace ServiceLib.Manager;

/// <summary>
/// Public, unauthenticated GitHub Releases distribution source for installed
/// Dirac Desktop. Private connection profiles are owner-distributed separately.
/// </summary>
public static class DiracGitHubReleaseConfig
{
    public const string RepositoryUrl = "https://github.com/Dimsho03/Dirac-Desktop";
    public const string StableChannel = "win-x64-stable";
    public const string BetaChannel = "win-x64-beta";

    public static string Channel(bool beta) => beta ? BetaChannel : StableChannel;

    public static bool IncludePrereleases(bool beta) => beta;

    // Never embed GitHub PATs or private repository credentials in the client.
    // The source repository or a separate release repository must be public
    // before friends can receive application updates through this endpoint.
}
