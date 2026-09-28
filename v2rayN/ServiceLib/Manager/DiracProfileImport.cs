using System.Text.Json.Nodes;

namespace ServiceLib.Manager;

/// <summary>
/// Imports owner-distributed, complete Dirac Xray TUN JSON without reserializing
/// the private profile. Ordinary vless:// links continue to use v2rayN's
/// existing generic importer.
/// </summary>
public static class DiracProfileImport
{
    public const int MaxProfileBytes = 1024 * 1024;
    public const string UnsupportedProfileMessage =
        "Unsupported Dirac profile. Import a complete original Dirac Xray TUN JSON configuration.";

    /// <summary>
    /// A Dirac profile must retain its original edge domain, WS path, TUN, and
    /// local DNS, so the direct-DoH bootstrap and default RU routing can run.
    /// Never include untrusted source JSON in validation errors or logs.
    /// </summary>
    public static bool IsCompatible(ReadOnlySpan<byte> raw)
    {
        if (raw.IsEmpty || raw.Length > MaxProfileBytes)
        {
            return false;
        }

        // A UTF-8 BOM is valid in a user-provided .json file. Remove it only
        // for validation; AddCustomServer copies the original file bytes.
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
        {
            raw = raw[3..];
        }

        try
        {
            var root = JsonNode.Parse(raw);
            return DiracDohBootstrap.IsEligible(root);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static async Task<ProfileItem> ImportFileAsync(
        Config config,
        string filePath,
        string? remarks = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(filePath)
            || !string.Equals(Path.GetExtension(filePath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(UnsupportedProfileMessage);
        }

        var file = new FileInfo(filePath);
        if (!file.Exists || file.Length is <= 0 or > MaxProfileBytes)
        {
            throw new InvalidDataException(UnsupportedProfileMessage);
        }

        var raw = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
        if (!IsCompatible(raw))
        {
            throw new InvalidDataException(UnsupportedProfileMessage);
        }

        // AddCustomServer copies the source file to private application data.
        // Do not delete or rewrite the file given to the user by the owner.
        return await ImportValidatedFileAsync(config, file.FullName, remarks, deleteSource: false);
    }

    public static async Task<ProfileItem> ImportClipboardAsync(
        Config config,
        string? clipboardText,
        string? remarks = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrEmpty(clipboardText) || clipboardText.Length > MaxProfileBytes)
        {
            throw new InvalidDataException(UnsupportedProfileMessage);
        }

        var raw = new UTF8Encoding(false, true).GetBytes(clipboardText);
        if (!IsCompatible(raw))
        {
            throw new InvalidDataException(UnsupportedProfileMessage);
        }

        var temp = Path.Combine(Path.GetTempPath(), "dirac-import-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            // Preserve the text exactly (apart from its UTF-8 encoding). In
            // particular, never pass it through a generic VLESS link parser.
            await File.WriteAllBytesAsync(temp, raw, cancellationToken);
            return await ImportValidatedFileAsync(config, temp, remarks, deleteSource: false);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static async Task<ProfileItem> ImportValidatedFileAsync(
        Config config,
        string filePath,
        string? remarks,
        bool deleteSource)
    {
        var item = new ProfileItem
        {
            IndexId = Guid.NewGuid().ToString("N"),
            ConfigType = EConfigType.Custom,
            CoreType = ECoreType.Xray,
            Address = filePath,
            Remarks = string.IsNullOrWhiteSpace(remarks) ? "Dirac" : remarks.Trim(),
            Subid = config.SubIndexId,
            IsSub = false
        };

        if (await ConfigHandler.AddCustomServer(config, item, deleteSource) != 0)
        {
            throw new IOException("Could not save the Dirac profile.");
        }

        return item;
    }
}
