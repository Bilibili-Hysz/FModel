using CUE4Parse.UE4.Assets.Exports;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.Identity;

/// <summary>
/// Converts a UObject path and its provider-fixed package path into one
/// canonical Unreal object identity without depending on a scene exporter.
/// </summary>
public static class FModelUnrealPathIdentity
{
    public static string BuildCanonicalObjectPath(UObject resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var pathName = resource.GetPathName().Replace('\\', '/');
        var colon = pathName.IndexOf(':');
        var packagePart = colon > 0 ? pathName[..colon] : pathName;
        var canonicalPackageObjectPath = IsCanonicalUnrealPath(packagePart)
            ? NormalizeCanonicalPackageObjectPath(packagePart)
            : BuildCanonicalPackageObjectPath(
                resource.Owner?.Provider?.FixPath(resource.Owner?.Name ?? packagePart) ?? packagePart);

        return colon > 0
            ? canonicalPackageObjectPath + ":" + pathName[(colon + 1)..]
            : canonicalPackageObjectPath;
    }

    private static string BuildCanonicalPackageObjectPath(string fixedPackagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixedPackagePath);
        var normalized = fixedPackagePath.Trim().Replace('\\', '/').Trim('/');
        if (normalized.Contains('\0') || normalized.Any(char.IsControl))
            throw new InvalidDataException("Package paths must not contain control characters.");

        var packagePath = ToUnrealPackagePath(RemovePackageExtension(normalized));
        var slash = packagePath.LastIndexOf('/');
        var packageName = slash >= 0 ? packagePath[(slash + 1)..] : packagePath;
        if (string.IsNullOrWhiteSpace(packageName))
            throw new InvalidDataException("Package paths must contain a package name.");
        return "/" + packagePath + "." + packageName;
    }

    private static string ToUnrealPackagePath(string value)
    {
        var normalized = value.Trim('/');
        if (normalized.StartsWith("Game/Content/", StringComparison.OrdinalIgnoreCase))
            return "Game/" + normalized["Game/Content/".Length..];
        if (normalized.StartsWith("Game/", StringComparison.OrdinalIgnoreCase))
            return "Game/" + normalized["Game/".Length..];
        if (normalized.StartsWith("Engine/Content/", StringComparison.OrdinalIgnoreCase))
            return "Engine/" + normalized["Engine/Content/".Length..];
        if (normalized.StartsWith("Engine/", StringComparison.OrdinalIgnoreCase))
            return "Engine/" + normalized["Engine/".Length..];
        if (normalized.StartsWith("Plugin/", StringComparison.OrdinalIgnoreCase))
            return "Plugin/" + normalized["Plugin/".Length..];

        const string contentMarker = "/Content/";
        var markerIndex = normalized.IndexOf(contentMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex > 0 && !normalized[..markerIndex].Contains('/'))
            return "Game/" + normalized[(markerIndex + contentMarker.Length)..];

        const string pluginsMarker = "/Plugins/";
        var pluginsIndex = normalized.IndexOf(pluginsMarker, StringComparison.OrdinalIgnoreCase);
        if (pluginsIndex >= 0)
        {
            var afterPlugins = normalized[(pluginsIndex + pluginsMarker.Length)..];
            var pluginContent = afterPlugins.IndexOf("/Content/", StringComparison.OrdinalIgnoreCase);
            if (pluginContent > 0)
            {
                var pluginName = afterPlugins[..pluginContent];
                var pluginPath = afterPlugins[(pluginContent + "/Content/".Length)..];
                return "Plugin/" + pluginName + "/" + pluginPath;
            }
        }

        throw new InvalidDataException($"Unsupported provider-fixed package path '{value}'.");
    }

    private static string NormalizeCanonicalPackageObjectPath(string value)
    {
        var normalized = value.Trim().Replace('\\', '/').Trim('/');
        var colon = normalized.IndexOf(':');
        if (colon >= 0)
            normalized = normalized[..colon];
        var slash = normalized.LastIndexOf('/');
        var leaf = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        if (leaf.LastIndexOf('.') <= 0)
            throw new InvalidDataException($"Canonical Unreal package path '{value}' has no object name.");
        return "/" + normalized;
    }

    private static string RemovePackageExtension(string value)
    {
        foreach (var extension in new[] { ".uasset", ".umap", ".uexp", ".ubulk" })
        {
            if (value.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return value[..^extension.Length];
        }
        return value;
    }

    private static bool IsCanonicalUnrealPath(string value) =>
        value.StartsWith("/Game/", StringComparison.Ordinal) ||
        value.StartsWith("/Engine/", StringComparison.Ordinal) ||
        value.StartsWith("/Plugin/", StringComparison.Ordinal);
}
