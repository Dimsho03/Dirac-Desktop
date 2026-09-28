using ServiceLib.Manager;

namespace ServiceLib.Tests.Manager;

public class DiracGitHubReleaseConfigTests
{
    [Test]
    public async Task StableIsDefaultForNewAndExistingInstallations()
    {
        var fresh = new CheckUpdateItem();
        var old = JsonSerializer.Deserialize<CheckUpdateItem>("{}")!;

        await fresh.DiracBetaChannel.Should().BeFalse();
        await old.DiracBetaChannel.Should().BeFalse();
        await (DiracGitHubReleaseConfig.Channel(old.DiracBetaChannel)
               == "win-x64-stable").Should().BeTrue();
        await DiracGitHubReleaseConfig.IncludePrereleases(false).Should().BeFalse();
    }

    [Test]
    public async Task BetaSelectionSurvivesSerialization()
    {
        var source = new CheckUpdateItem { DiracBetaChannel = true };
        var restored = JsonSerializer.Deserialize<CheckUpdateItem>(JsonSerializer.Serialize(source))!;

        await restored.DiracBetaChannel.Should().BeTrue();
        await (DiracGitHubReleaseConfig.Channel(restored.DiracBetaChannel)
               == "win-x64-beta").Should().BeTrue();
        await DiracGitHubReleaseConfig.IncludePrereleases(true).Should().BeTrue();
    }

    [Test]
    public async Task CorePrereleaseChoiceDoesNotChangeApplicationUpdateChannel()
    {
        var config = new CheckUpdateItem
        {
            CheckPreReleaseUpdate = true,
            DiracBetaChannel = false
        };

        await DiracGitHubReleaseConfig.Channel(config.DiracBetaChannel)
            .Should().Be(DiracGitHubReleaseConfig.StableChannel);
    }

    [Test]
    public async Task GitHubOriginIsPublicHttpsUrlAndNoTokenOrOwnerProfile()
    {
        var url = new Uri(DiracGitHubReleaseConfig.RepositoryUrl);
        await (url.Scheme == Uri.UriSchemeHttps).Should().BeTrue();
        await (url.Host == "github.com").Should().BeTrue();
        await (url.AbsolutePath == "/Dimsho03/Dirac-Desktop").Should().BeTrue();
        await (url.UserInfo == string.Empty).Should().BeTrue();
        await (url.Query == string.Empty).Should().BeTrue();
        await (url.Fragment == string.Empty).Should().BeTrue();
    }
}
