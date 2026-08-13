using CUE4Parse_Conversion.UEScene;
using CUE4Parse_Conversion.Formats.World;
using CUE4Parse.UE4.Assets.Exports;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.World;

/// <summary>Strict world-format path policy. It returns Content-relative logical URIs.</summary>
public sealed class UeSceneWorldPathResolver : IWorldExportPathResolver
{
    public static UeSceneWorldPathResolver Instance { get; } = new();

    private UeSceneWorldPathResolver()
    {
    }

    public string Resolve(UObject resource, string fromDirectory, WorldExportAssetKind kind)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var canonicalPath = BuildCanonicalObjectPath(resource);
        return BuildResourceUri(canonicalPath, kind, kind switch
        {
            WorldExportAssetKind.World => ".umap.uescene",
            WorldExportAssetKind.Mesh => ".uemodel",
            WorldExportAssetKind.Material => ".json",
            WorldExportAssetKind.Texture => ".png",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        });
    }

    public static string BuildCanonicalObjectPath(UObject resource) =>
        UeSceneWorldPathIdentity.BuildCanonicalObjectPath(resource);

    public static string BuildResourceUri(string canonicalUnrealObjectPath, WorldExportAssetKind kind, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalUnrealObjectPath);
        return kind switch
        {
            WorldExportAssetKind.World => UESceneProjectExportUri.FromUnrealPath(canonicalUnrealObjectPath, extension),
            WorldExportAssetKind.Mesh => new UESceneResourceUriPolicy(UESceneResourceLogicalRoot.GameExportTree)
                .Allocate(canonicalUnrealObjectPath, UESceneResourceKind.Model, extension),
            WorldExportAssetKind.Material => new UESceneResourceUriPolicy(UESceneResourceLogicalRoot.GameExportTree)
                .Allocate(canonicalUnrealObjectPath, UESceneResourceKind.Material, extension),
            WorldExportAssetKind.Texture => new UESceneResourceUriPolicy(UESceneResourceLogicalRoot.GameExportTree)
                .Allocate(canonicalUnrealObjectPath, UESceneResourceKind.Texture, extension),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
