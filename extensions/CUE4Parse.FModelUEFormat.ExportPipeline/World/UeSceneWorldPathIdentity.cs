using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse_Conversion.Dto;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.World;

/// <summary>
/// Converts the two path identities exposed by CUE4Parse into one canonical
/// Unreal object identity. ObjectDto.Path is a provider-fixed physical package
/// path (for example Game/Content/Maps/Login.umap), while UObject.GetPathName()
/// is already a logical Unreal path. World planning must not mix those forms.
/// </summary>
public static class UeSceneWorldPathIdentity
{
    public static string BuildCanonicalObjectPath(ObjectDto source, string fallbackObjectName)
    {
        ArgumentNullException.ThrowIfNull(source);
        return BuildCanonicalObjectPath(source.Path, source.OuterNames, fallbackObjectName);
    }

    public static string BuildCanonicalObjectPath(string fixedPackagePath, IReadOnlyList<string>? outerNames, string fallbackObjectName)
    {
        var packageObjectPath = BuildCanonicalPackageObjectPath(fixedPackagePath);
        var outerChain = outerNames is { Count: > 0 }
            ? string.Join('.', outerNames)
            : string.Empty;

        if (!string.IsNullOrWhiteSpace(outerChain))
            return packageObjectPath + ":" + outerChain;

        // A top-level UWorld has no ':' chain. For DTOs that lost their source
        // chain, retain a deterministic fallback without confusing an actor label
        // with the package-level object name.
        var packageName = packageObjectPath[(packageObjectPath.LastIndexOf('.') + 1)..];
        return string.Equals(packageName, fallbackObjectName, StringComparison.Ordinal)
            ? packageObjectPath
            : packageObjectPath + ":" + fallbackObjectName;
    }

    public static string BuildCanonicalPackageObjectPath(string fixedPackagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixedPackagePath);
        var normalized = fixedPackagePath.Trim().Replace('\\', '/').Trim('/');
        if (normalized.Contains('\0'))
            throw new InvalidDataException("World package paths must not contain NUL.");

        var packagePath = ToUnrealPackagePath(RemovePackageExtension(normalized));
        var slash = packagePath.LastIndexOf('/');
        var packageName = slash >= 0 ? packagePath[(slash + 1)..] : packagePath;
        if (string.IsNullOrWhiteSpace(packageName))
            throw new InvalidDataException("World package paths must contain a package name.");

        return "/" + packagePath + "." + packageName;
    }

    public static string BuildCanonicalObjectPath(UObject resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var pathName = resource.GetPathName().Replace('\\', '/');
        var colon = pathName.IndexOf(':');
        var packagePart = colon > 0 ? pathName[..colon] : pathName;
        var canonicalPackageObjectPath = IsCanonicalUnrealPath(packagePart)
            ? NormalizeCanonicalPackageObjectPath(packagePart)
            : BuildCanonicalPackageObjectPath(resource.Owner?.Provider?.FixPath(resource.Owner?.Name ?? packagePart) ?? packagePart);

        return colon > 0
            ? canonicalPackageObjectPath + ":" + pathName[(colon + 1)..]
            : canonicalPackageObjectPath;
    }

    private static string ToUnrealPackagePath(string value)
    {
        var normalized = value.Trim('/');
        if (normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = normalized.TrimStart('/');

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

        // Provider.FixPath prefixes project content with <ProjectName>/Content.
        // Treat that prefix as the physical representation of /Game.
        var contentMarker = "/Content/";
        var markerIndex = normalized.IndexOf(contentMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex > 0 && !normalized[..markerIndex].Contains('/'))
            return "Game/" + normalized[(markerIndex + contentMarker.Length)..];

        // Plugin packages are commonly fixed as
        // <Project>/Plugins/<Plugin>/Content/<Path>.
        var pluginsMarker = "/Plugins/";
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

        throw new InvalidDataException($"Unsupported provider-fixed world package path '{value}'.");
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
        value.StartsWith("/Game/", StringComparison.Ordinal)
        || value.StartsWith("/Engine/", StringComparison.Ordinal)
        || value.StartsWith("/Plugin/", StringComparison.Ordinal);
}
