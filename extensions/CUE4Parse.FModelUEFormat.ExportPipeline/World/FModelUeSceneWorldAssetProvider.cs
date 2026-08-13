using System.Security.Cryptography;
using CUE4Parse;
using System.Text;
using Serilog;
using CUE4Parse.FModelUEFormat.ExportPipeline.Materials;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Exporters;
using CUE4Parse_Conversion.Formats.World;
using CUE4Parse_Conversion.UEScene;
using CUE4Parse_Conversion.UEScene.World;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.World;

/// <summary>
/// P3 bridge from queue-owned upstream/V3 exporters to UEScene resource records.
/// BuildExportFilesForProjection() is used for every snapshot, so the bytes and
/// integrity metadata are exactly the bytes that ExportSession later writes.
/// </summary>
public sealed class FModelUeSceneWorldAssetProvider : IUeSceneWorldAssetProvider
{
    private readonly ExportSession session;
    private readonly FModelUeFormatPolicy policy;
    private readonly Dictionary<ulong, IReadOnlyList<UeSceneWorldAssetPlan>> dependenciesByAssetId = [];

    public FModelUeSceneWorldAssetProvider(ExportSession session, FModelUeFormatPolicy policy)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
        if (this.policy.ResourceUriRoot != FModelUeFormatResourceUriRoot.GameExportTree)
            throw new ArgumentException("UEScene world resources require the Game export-tree URI root.", nameof(policy));
    }

    public bool TryDescribe(UObject resource, WorldExportAssetKind kind, string logicalUri, out UeSceneWorldAssetPlan asset)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (kind != WorldExportAssetKind.Mesh || resource is not UStaticMesh mesh)
        {
            asset = null!;
            return false;
        }

        try
        {
            var canonicalPath = UeSceneWorldPathResolver.BuildCanonicalObjectPath(mesh);
            // Keep the official StaticMeshExporter as the canonical queue root.
            // MeshExporter selects the V3 format only while this UEFormat session runs.
            var exporter = session.AddOrGet(new StaticMeshExporter(mesh));
            if (exporter is not StaticMeshExporter)
            {
                CUE4ParseLog.Log.Warning(
                    "V3 UEScene mesh skipped because the canonical queue root is not the official StaticMeshExporter for {ResourcePath}",
                    resource.GetPathName());
                asset = null!;
                return false;
            }

            var files = exporter.BuildExportFilesForProjection();
            if (!files.Any(file => string.Equals(file.Extension, "uemodel", StringComparison.OrdinalIgnoreCase)))
            {
                asset = null!;
                return false;
            }

            var modelFile = files.First(file => string.Equals(file.Extension, "uemodel", StringComparison.OrdinalIgnoreCase));
            if (modelFile.Data.Length == 0)
            {
                asset = null!;
                return false;
            }

            var resolvedUri = FModelMaterialUriResolver.ResolveResourceUri(
                mesh,
                modelFile.Extension,
                modelFile.NameSuffix,
                policy.ResourceUriRoot);
            var assetId = StableId("mesh", canonicalPath);
            asset = new UeSceneWorldAssetPlan(
                assetId,
                AssetKind.StaticMesh,
                LocationMode.Bundle,
                Availability.Available,
                canonicalPath,
                mesh.Name,
                RequireMatchedUeSceneUri(resolvedUri, logicalUri),
                "application/x-uemodel",
                SHA256.HashData(modelFile.Data),
                checked((ulong)modelFile.Data.LongLength),
                BundleResourceKind.Model,
                Diagnostics: Array.Empty<UeSceneWorldDiagnosticPlan>());

            dependenciesByAssetId[assetId] = BuildMaterialAndTextureDependencies(mesh);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogProviderFailure("mesh.describe", resource, exception);
            asset = null!;
            return false;
        }
    }

    public IReadOnlyList<UeSceneWorldAssetPlan> GetDependencies(UeSceneWorldAssetPlan asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return dependenciesByAssetId.TryGetValue(asset.AssetId, out var dependencies)
            ? dependencies
            : Array.Empty<UeSceneWorldAssetPlan>();
    }

    private IReadOnlyList<UeSceneWorldAssetPlan> BuildMaterialAndTextureDependencies(UStaticMesh mesh)
    {
        // Respect the upstream ExportMaterials option. If disabled, no material
        // or texture sidecar is allowed to be advertised as an available bundle
        // resource even if a material projection can be inspected.
        if (!session.CurrentOptions.ExportMaterials)
            return Array.Empty<UeSceneWorldAssetPlan>();

        var dependencies = new Dictionary<ulong, UeSceneWorldAssetPlan>();
        foreach (var staticMaterial in mesh.StaticMaterials)
        {
            if (staticMaterial.MaterialInterface?.TryLoad<UMaterialInterface>(out var material) != true || material is null)
                continue;

            var materialAsset = TryBuildMaterialAsset(material);
            if (materialAsset is not null)
                dependencies[materialAsset.AssetId] = materialAsset;

            foreach (var textureAsset in BuildTextureAssets(material))
                dependencies[textureAsset.AssetId] = textureAsset;
        }

        return dependencies.Values
            .OrderBy(dependency => dependency.CanonicalUnrealPath, StringComparer.Ordinal)
            .ThenBy(dependency => dependency.LogicalUri, StringComparer.Ordinal)
            .ToArray();
    }

    private UeSceneWorldAssetPlan? TryBuildMaterialAsset(UMaterialInterface material)
    {
        try
        {
            var exporter = session.AddOrGet(new MaterialExporter(material));
            var files = exporter.BuildExportFilesForProjection();
            if (!files.Any(candidate => string.Equals(candidate.Extension, "json", StringComparison.OrdinalIgnoreCase)))
                return null;
            var file = files.First(candidate => string.Equals(candidate.Extension, "json", StringComparison.OrdinalIgnoreCase));
            if (file.Data.Length == 0)
                return null;

            var canonicalPath = UeSceneWorldPathResolver.BuildCanonicalObjectPath(material);
            var uri = FModelMaterialUriResolver.ResolveResourceUri(
                material,
                file.Extension,
                file.NameSuffix,
                policy.ResourceUriRoot);
            return CreateAsset(
                "material",
                AssetKind.Material,
                BundleResourceKind.MaterialJson,
                canonicalPath,
                material.Name,
                uri,
                "application/json",
                file.Data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Engine/plugin sidecars are deliberately not advertised as available
            // Game-tree BNDX entries until a physical publication policy exists.
            LogProviderFailure("material.dependency", material, exception);
            return null;
        }
    }

    private IEnumerable<UeSceneWorldAssetPlan> BuildTextureAssets(UMaterialInterface material)
    {
        var parameters = new CMaterialParams2();
        try
        {
            material.GetParams(parameters, session.CurrentOptions.MaterialDepth);
        }
        catch (Exception exception)
        {
            LogProviderFailure("material.parameters", material, exception);
            yield break;
        }

        foreach (var source in parameters.Textures.Values.OfType<UTexture>())
        {
            IReadOnlyList<ExportFile> files;
            try
            {
                var exporter = session.AddOrGet(new TextureExporter(source));
                files = exporter.BuildExportFilesForProjection();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogProviderFailure("texture.dependency", source, exception);
                continue;
            }

            foreach (var file in files)
            {
                if (file.Data.Length == 0)
                    continue;

                var extension = "." + file.Extension.Trim().TrimStart('.').ToLowerInvariant();
                var canonicalPath = UeSceneWorldPathResolver.BuildCanonicalObjectPath(source);
                string uri;
                try
                {
                    uri = FModelMaterialUriResolver.ResolveResourceUri(
                        source,
                        file.Extension,
                        file.NameSuffix,
                        policy.ResourceUriRoot);
                }
                catch (ArgumentException exception)
                {
                    // See the material-sidecar note above. A missing optional
                    // texture must not poison the world-level BNDX validation.
                    LogProviderFailure("texture.uri", source, exception);
                    continue;
                }

                yield return CreateAsset(
                    "texture",
                    AssetKind.Texture,
                    BundleResourceKind.Texture,
                    canonicalPath,
                    source.Name,
                    uri,
                    MimeFromExtension(extension),
                    file.Data);
            }
        }
    }

    private static UeSceneWorldAssetPlan CreateAsset(
        string domain,
        AssetKind kind,
        BundleResourceKind bundleKind,
        string canonicalPath,
        string displayName,
        string logicalUri,
        string mime,
        byte[] bytes) => new(
            StableId(domain, canonicalPath + "\0" + logicalUri),
            kind,
            LocationMode.Bundle,
            Availability.Available,
            canonicalPath,
            displayName,
            NormalizeUeSceneUri(logicalUri),
            mime,
            SHA256.HashData(bytes),
            checked((ulong)bytes.LongLength),
            bundleKind,
            Diagnostics: Array.Empty<UeSceneWorldDiagnosticPlan>());

    private static string RequireMatchedUeSceneUri(string producerUri, string plannedUri)
    {
        var normalizedProducer = NormalizeUeSceneUri(producerUri);
        var normalizedPlanned = NormalizeUeSceneUri(plannedUri);
        if (!string.Equals(normalizedProducer, normalizedPlanned, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"UEScene resource URI disagrees with the queued producer path: '{normalizedPlanned}' vs '{normalizedProducer}'.");
        }
        return normalizedProducer;
    }

    private static string NormalizeUeSceneUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new InvalidDataException("UEScene resource URI is required.");

        var normalized = uri.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith("Content/", StringComparison.Ordinal))
            throw new InvalidDataException($"UEScene resource URI must be rooted at Content/: {uri}");
        return UESceneResourceUriPolicy.NormalizeLogicalUri(normalized);
    }

    private static void LogProviderFailure(string stage, UObject resource, Exception exception)
    {
        CUE4ParseLog.Log.Warning(
            exception,
            "V3 UEScene dependency projection skipped at {Stage} for {ResourcePath}",
            stage,
            resource.GetPathName());
    }

    private static string MimeFromExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".tga" => "image/x-tga",
        ".webp" => "image/webp",
        ".hdr" => "image/vnd.radiance",
        _ => "application/octet-stream"
    };

    private static ulong StableId(string domain, string identity)
    {
        var seed = Encoding.UTF8.GetBytes(domain + "\0" + identity);
        var value = BitConverter.ToUInt64(SHA256.HashData(seed), 0);
        return value == 0 ? 1UL : value;
    }
}
