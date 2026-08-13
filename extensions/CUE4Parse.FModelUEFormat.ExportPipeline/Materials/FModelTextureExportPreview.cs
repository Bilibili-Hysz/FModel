using System.Security.Cryptography;
using System.Text;
using CUE4Parse;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UeFormat.UEModel;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Exporters;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.Materials;

/// <summary>
/// Bridges the queue-owned upstream TextureExporter to the V3 projection pass.
/// It invokes the same BuildExportFiles implementation that ExporterBase later writes,
/// caches that result on the upstream exporter, and returns only deterministic metadata.
/// No file is written here.
/// </summary>
public sealed class FModelTextureExportPreview(
    ExportSession session,
    FModelUeFormatResourceUriRoot resourceUriRoot = FModelUeFormatResourceUriRoot.ExportRoot,
    string? ownerModelUri = null)
{
    private readonly ExportSession session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly FModelUeFormatResourceUriRoot resourceUriRoot = Enum.IsDefined(resourceUriRoot)
        ? resourceUriRoot
        : throw new ArgumentOutOfRangeException(nameof(resourceUriRoot));

    public bool TryProject(UTexture texture, out IReadOnlyList<FModelTextureOutputProjection> outputs)
    {
        ArgumentNullException.ThrowIfNull(texture);

        try
        {
            var exporter = session.AddOrGet(new TextureExporter(texture));
            if (exporter is not TextureExporter textureExporter)
            {
                outputs = Array.Empty<FModelTextureOutputProjection>();
                return false;
            }

            var files = textureExporter.BuildExportFilesForProjection();
            if (files.Count == 0)
            {
                outputs = Array.Empty<FModelTextureOutputProjection>();
                return false;
            }

            outputs = files
                .Select((file, outputOrdinal) =>
                {
                    var uri = FModelMaterialUriResolver.ResolveTextureUri(texture, file.Extension, file.NameSuffix, resourceUriRoot, ownerModelUri);
                    var stableId = BuildStableId(texture.GetPathName(), uri);
                    return new FModelTextureOutputProjection(
                        stableId,
                        uri,
                        file.Data.Length,
                        SHA256.HashData(file.Data),
                        IsEmbedded: false,
                        OutputOrdinal: outputOrdinal,
                        LocationMode: LocationMode(resourceUriRoot));
                })
                .ToArray();

            return outputs.Count > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            CUE4ParseLog.Log.Warning(
                exception,
                "V3 texture projection skipped for {TexturePath}",
                texture.GetPathName());
            outputs = Array.Empty<FModelTextureOutputProjection>();
            return false;
        }
    }

    private static FModelTextureResourceLocation LocationMode(FModelUeFormatResourceUriRoot root) => root switch
    {
        FModelUeFormatResourceUriRoot.RelativeToOwner => FModelTextureResourceLocation.Relative,
        FModelUeFormatResourceUriRoot.GameExportTree => FModelTextureResourceLocation.ExportRoot,
        FModelUeFormatResourceUriRoot.ExportRoot => FModelTextureResourceLocation.ExportRoot,
        _ => throw new ArgumentOutOfRangeException(nameof(root))
    };

    private static string BuildStableId(string sourceIdentity, string logicalResourceUri)
    {
        var seed = Encoding.UTF8.GetBytes(sourceIdentity + "\0" + logicalResourceUri);
        return "tex." + Convert.ToHexString(SHA256.HashData(seed)).ToLowerInvariant();
    }
}
