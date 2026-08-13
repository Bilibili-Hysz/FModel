using System;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.FModelUEFormat.ExportPipeline.Identity;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UeFormat.Protocol;
using CUE4Parse_Conversion.Options;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.Materials;

/// <summary>
/// Reproduces the new ExporterBase SavePath rules for logical resource URIs without
/// taking ownership of filesystem writes. Standalone exports preserve the upstream
/// export-root form; the retained GameExportTree compatibility policy projects
/// project-owned assets into a <c>Content/</c> namespace without scene dependencies.
/// </summary>
public static class FModelMaterialUriResolver
{
    public static string ResolveMaterialJsonUri(
        UMaterialInterface material,
        FModelUeFormatResourceUriRoot resourceUriRoot = FModelUeFormatResourceUriRoot.ExportRoot,
        string? ownerModelUri = null)
    {
        ArgumentNullException.ThrowIfNull(material);
        return ResolveResourceUri(material, "json", resourceUriRoot: resourceUriRoot, ownerModelUri: ownerModelUri);
    }

    /// <summary>
    /// Compatibility overload used before the actual TextureExporter output is known.
    /// </summary>
    public static string ResolveTextureUri(
        UUnrealMaterial texture,
        ExportOptions options,
        FModelUeFormatResourceUriRoot resourceUriRoot = FModelUeFormatResourceUriRoot.ExportRoot,
        string? ownerModelUri = null)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(options);
        return ResolveResourceUri(texture, GetTextureExtension(options), resourceUriRoot: resourceUriRoot, ownerModelUri: ownerModelUri);
    }

    /// <summary>
    /// Resolves the exact logical path emitted by ExporterBase for an ExportFile.
    /// The extension is supplied by TextureExporter so HDR output is represented as
    /// <c>.hdr</c> even when the selected encoder format is PNG/JPG/TGA/WebP.
    /// </summary>
    public static string ResolveTextureUri(
        UUnrealMaterial texture,
        string extension,
        string? nameSuffix = null,
        FModelUeFormatResourceUriRoot resourceUriRoot = FModelUeFormatResourceUriRoot.ExportRoot,
        string? ownerModelUri = null)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return ResolveResourceUri(texture, extension, nameSuffix, resourceUriRoot, ownerModelUri);
    }

    public static string ResolveResourceUri(
        UObject resource,
        string extension,
        string? nameSuffix = null,
        FModelUeFormatResourceUriRoot resourceUriRoot = FModelUeFormatResourceUriRoot.ExportRoot,
        string? ownerModelUri = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (string.IsNullOrWhiteSpace(extension))
            throw new ArgumentException("An extension is required.", nameof(extension));
        if (!Enum.IsDefined(resourceUriRoot))
            throw new ArgumentOutOfRangeException(nameof(resourceUriRoot));

        var normalizedExtension = extension.Trim().TrimStart('.').ToLowerInvariant();
        if (normalizedExtension.Length == 0 || normalizedExtension.Contains('/') || normalizedExtension.Contains('\\') || normalizedExtension.Contains(':'))
            throw new ArgumentException("The extension must be a simple logical extension.", nameof(extension));

        var normalizedSuffix = NormalizeNameSuffix(nameSuffix);
        var exportRootUri = NormalizeLogicalUri(BuildSavePath(resource) + normalizedSuffix + "." + normalizedExtension);
        return ApplyResourceUriRoot(resource, exportRootUri, resourceUriRoot, ownerModelUri);
    }

    /// <summary>Returns a safe relative URI from an owner file to a target file.</summary>
    public static string ResolveRelativeUri(string ownerFileUri, string targetFileUri) =>
        LogicalResourceUri.ResolveRelative(ownerFileUri, targetFileUri);

    public static string NormalizeResourceUri(string value, bool allowLeadingParents = false) =>
        LogicalResourceUri.Normalize(value, allowLeadingParents);

    public static string NormalizeLogicalUri(string value) =>
        LogicalResourceUri.Normalize(value);

    private static string ApplyResourceUriRoot(
        UObject resource,
        string exportRootUri,
        FModelUeFormatResourceUriRoot resourceUriRoot,
        string? ownerModelUri)
    {
        if (resourceUriRoot == FModelUeFormatResourceUriRoot.ExportRoot)
            return exportRootUri;
        if (resourceUriRoot == FModelUeFormatResourceUriRoot.RelativeToOwner)
        {
            if (string.IsNullOrWhiteSpace(ownerModelUri))
                throw new ArgumentException("Owner-relative resource URIs require the final UEMODEL URI.", nameof(ownerModelUri));
            return ResolveRelativeUri(ownerModelUri, exportRootUri);
        }

        // Establish ownership from the canonical Unreal identity before projecting
        // the provider-fixed output path. This accepts arbitrary project mounts
        // such as CodeWorld/Content while keeping Engine and plugin resources out
        // of the project's Content tree.
        var canonicalObjectPath = FModelUnrealPathIdentity.BuildCanonicalObjectPath(resource);
        if (!FModelProjectExportUri.IsContentRootedUnrealPath(canonicalObjectPath))
            throw new ArgumentException("Game export-tree URIs require a project Content resource.", nameof(resource));

        return NormalizeLogicalUri(FModelProjectExportUri.FromFixedExportPath(exportRootUri));
    }

    private static string NormalizeNameSuffix(string? nameSuffix)
    {
        if (string.IsNullOrEmpty(nameSuffix))
            return string.Empty;
        if (nameSuffix.Contains('\0') || nameSuffix.Contains('/') || nameSuffix.Contains('\\') || nameSuffix.Contains(':'))
            throw new ArgumentException("The name suffix must be a simple logical suffix.", nameof(nameSuffix));
        return nameSuffix;
    }

    private static string GetTextureExtension(ExportOptions options)
    {
        // This is only a fallback prediction. FModelTextureExportPreview binds the
        // final ExportFile.Extension, including HDR output selected by TextureEncoder.
        return options.TextureFormat switch
        {
            ETextureFormat.Png => "png",
            ETextureFormat.Jpeg => "jpg",
            ETextureFormat.Tga => "tga",
            ETextureFormat.Webp => "webp",
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.TextureFormat, "Unsupported texture format.")
        };
    }

    private static string BuildSavePath(UObject resource)
    {
        var packagePath = BuildPackagePath(resource);
        var lastSlash = packagePath.LastIndexOf('/');
        var leaf = lastSlash >= 0 ? packagePath[(lastSlash + 1)..] : packagePath;
        var savePath = leaf.Equals(resource.Name, StringComparison.OrdinalIgnoreCase)
            ? packagePath
            : packagePath + "/" + resource.Name;
        return savePath.TrimStart('/');
    }

    private static string BuildPackagePath(UObject resource)
    {
        var owner = resource.Owner;
        var pathName = resource.GetPathName();
        var rawPath = owner?.Name ?? pathName;
        var fixedPath = (owner?.Provider?.FixPath(rawPath) ?? rawPath).Replace('\\', '/');
        var lastDot = fixedPath.LastIndexOf('.');
        var basePath = lastDot >= 0 ? fixedPath[..lastDot] : fixedPath;

        var colonIndex = pathName.IndexOf(':');
        if (colonIndex > 0)
        {
            var subChain = pathName[(colonIndex + 1)..];
            var lastSubDot = subChain.LastIndexOf('.');
            if (lastSubDot > 0)
                basePath += "/" + subChain[..lastSubDot].Replace('.', '/');
        }

        return basePath;
    }
}
