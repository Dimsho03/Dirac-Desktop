namespace ServiceLib.Manager;

public static class DiracPinnedCore
{
    public const string MarkerFileName = "dirac-core.sha256";
    public static bool IsPinned(string directory) => File.Exists(Path.Combine(directory, MarkerFileName));
    public static bool Matches(string executable, string directory)
    {
        try
        {
            var marker = Path.Combine(directory, MarkerFileName);
            if (!File.Exists(marker) || !File.Exists(executable)) return false;
            var expected = File.ReadAllText(marker).Trim();
            if (expected.Length != 64 || !Regex.IsMatch(expected, @"\A[0-9a-fA-F]{64}\z")) return false;
            using var source = File.OpenRead(executable);
            return string.Equals(expected, Convert.ToHexString(SHA256.HashData(source)), StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
