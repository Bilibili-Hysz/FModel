using System.Collections.Immutable;
using CUE4Parse.FModelUEFormat.ExportPipeline.Diagnostics;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.UeFormat.UEModel;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.Materials;

/// <summary>
/// Material/texture projection contract shared by the upstream adapter and the
/// append-only UEFormat writer. The standard MATERIALS chunk remains upstream-owned;
/// these records only describe deterministic V3 links and output metadata.
/// </summary>
public sealed record FModelMaterialProjection(
    string ObjectPath,
    IReadOnlyList<FModelMaterialLinkProjection> Materials,
    IReadOnlyList<FModelTextureLinkProjection> Textures,
    IReadOnlyList<FModelUeFormatPipelineDiagnostic> Diagnostics,
    IReadOnlyList<FModelTextureResourceProjection>? Resources = null)
{
    public IReadOnlyList<FModelTextureResourceProjection> TextureResources { get; } =
        Resources ?? Array.Empty<FModelTextureResourceProjection>();

    public bool IsComplete => Diagnostics.All(diagnostic => diagnostic.Severity != FModelUeFormatDiagnosticSeverity.Error);
}

public sealed record FModelMaterialLinkProjection(
    int SlotIndex,
    int SourceMaterialIndex,
    string SlotName,
    string SourceIdentity,
    string MaterialJsonUri);

/// <summary>One material parameter binding to the first exported texture output.</summary>
public sealed record FModelTextureLinkProjection(
    int MaterialSlotIndex,
    string ParameterIdentity,
    string SourceIdentity,
    string TextureUri,
    string? TextureResourceId = null,
    PbrMapKind? MapKind = null,
    bool IsSrgb = false,
    string? MaterialId = null);

/// <summary>Metadata for one actual upstream TextureExporter ExportFile.</summary>
public sealed record FModelTextureResourceProjection(
    string StableId,
    string SourceIdentity,
    string LogicalResourceUri,
    int ByteLength,
    IReadOnlyList<byte> Sha256,
    bool IsEmbedded = false,
    FModelTextureResourceLocation LocationMode = FModelTextureResourceLocation.ExportRoot)
{
    public ImmutableArray<byte> Sha256Bytes => Sha256.ToImmutableArray();
}

/// <summary>Projection-time representation of one actual encoded texture file.</summary>
public sealed record FModelTextureOutputProjection(
    string StableId,
    string LogicalResourceUri,
    int ByteLength,
    IReadOnlyList<byte> Sha256,
    bool IsEmbedded = false,
    int OutputOrdinal = 0,
    FModelTextureResourceLocation LocationMode = FModelTextureResourceLocation.ExportRoot);
