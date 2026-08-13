using System.Text.RegularExpressions;
using System.Collections.Immutable;
using CUE4Parse.UeFormat.Protocol;
using CUE4Parse.UeFormat.UEModel;

namespace CUE4Parse.UeFormat.Export;

public enum ProducerKind
{
    Uemodel,
    Ueanim,
    Uepose
}

/// <summary>Immutable opaque standard chunk copied at the producer boundary.</summary>
public sealed record ProducerStandardChunk
{
    public ProducerStandardChunk(string chunkId, byte[] payload)
    {
        ChunkId = chunkId ?? throw new ArgumentNullException(nameof(chunkId));
        _payload = (payload ?? throw new ArgumentNullException(nameof(payload))).ToArray();
    }

    private readonly byte[] _payload;
    public string ChunkId { get; }
    public ReadOnlyMemory<byte> Payload => _payload.ToArray();
}

/// <summary>Material slots declared by an individual model LOD in their source order.</summary>
public sealed record LodMaterialTopologySnapshot
{
    public LodMaterialTopologySnapshot(int lodIndex, IEnumerable<string> materialIds)
    {
        LodIndex = lodIndex;
        MaterialIds = Array.AsReadOnly((materialIds ?? throw new ArgumentNullException(nameof(materialIds))).ToArray());
    }

    public int LodIndex { get; }
    public IReadOnlyList<string> MaterialIds { get; }
}

/// <summary>
/// Read-only standard UEMODEL topology observed by a real producer or inspector.
/// This is metadata only: standard LODS and SKELETON bytes remain opaque and are never encoded here.
/// </summary>
public sealed record ProducerTopologySnapshot
{
    public ProducerTopologySnapshot(IEnumerable<InspectedLodTopology> lods, InspectedSkeletonTopology? skeleton)
    {
        Lods = Array.AsReadOnly((lods ?? throw new ArgumentNullException(nameof(lods)))
            .Select(lod => lod is null
                ? throw new ArgumentException("Topology LODs cannot contain null entries.", nameof(lods))
                : new InspectedLodTopology(lod.LodIndex, lod.VertexCount, lod.IndexCount, lod.Indices, lod.MaterialSlots))
            .OrderBy(lod => lod.LodIndex)
            .ToArray());
        Skeleton = skeleton is null
            ? null
            : new InspectedSkeletonTopology(skeleton.Bones, skeleton.BoneCount, skeleton.BoneLayoutHash, skeleton.Identity);
    }

    public IReadOnlyList<InspectedLodTopology> Lods { get; }
    public InspectedSkeletonTopology? Skeleton { get; }
}

/// <summary>
/// Explicit producer input. Standard chunks are opaque snapshots and are never decoded or rewritten.
/// Extension versions are part of the public contract and currently must equal 1.
/// </summary>
public sealed record ProducerSnapshot
{
    public const int CurrentExtensionVersion = 1;

    public ProducerSnapshot(
        ProducerKind kind,
        IEnumerable<ProducerStandardChunk> standardChunks,
        IEnumerable<MaterialLinkRecord> materialLinks,
        SkeletonIdentityV1? skeletonIdentity,
        IEnumerable<LodMaterialTopologySnapshot> lodMaterialTopologies,
        TextureResourceSet? textureResourceSet = null,
        int materialExtensionVersion = CurrentExtensionVersion,
        int skeletonExtensionVersion = CurrentExtensionVersion,
        ProducerTopologySnapshot? topologySnapshot = null)
    {
        Kind = kind;
        StandardChunks = Array.AsReadOnly((standardChunks ?? throw new ArgumentNullException(nameof(standardChunks)))
            .Select(chunk => chunk is null ? throw new ArgumentException("Standard chunks cannot contain null entries.", nameof(standardChunks)) : new ProducerStandardChunk(chunk.ChunkId, chunk.Payload.ToArray())).ToArray());
        MaterialLinks = Array.AsReadOnly((materialLinks ?? throw new ArgumentNullException(nameof(materialLinks))).ToArray());
        SkeletonIdentity = skeletonIdentity is null ? null : new SkeletonIdentityV1(skeletonIdentity.SkeletonPath, skeletonIdentity.SkeletonGuid, skeletonIdentity.BoneLayoutHash);
        LodMaterialTopologies = Array.AsReadOnly((lodMaterialTopologies ?? throw new ArgumentNullException(nameof(lodMaterialTopologies)))
            .Select(topology => topology is null ? throw new ArgumentException("LOD material topologies cannot contain null entries.", nameof(lodMaterialTopologies)) : new LodMaterialTopologySnapshot(topology.LodIndex, topology.MaterialIds)).ToArray());
        TextureResourceSet = textureResourceSet is null
            ? null
            : new TextureResourceSet(
                textureResourceSet.Textures.Select(texture => texture is null
                    ? throw new ArgumentException("Texture resources must not contain null entries.", nameof(textureResourceSet))
                    : new TextureResource(texture.StableId, texture.LogicalResourceUri, texture.ByteLength, texture.Sha256, texture.IsEmbedded, texture.LocationMode)).ToImmutableArray(),
                textureResourceSet.Bindings.Select(binding => binding is null
                    ? throw new ArgumentException("Texture bindings must not contain null entries.", nameof(textureResourceSet))
                    : new PbrTextureBinding(binding.MaterialId, binding.MapKind, binding.TextureResourceId, binding.IsSrgb)).ToImmutableArray());
        MaterialExtensionVersion = materialExtensionVersion;
        SkeletonExtensionVersion = skeletonExtensionVersion;
        TopologySnapshot = topologySnapshot is null
            ? null
            : new ProducerTopologySnapshot(topologySnapshot.Lods, topologySnapshot.Skeleton);
    }

    public ProducerKind Kind { get; }
    public IReadOnlyList<ProducerStandardChunk> StandardChunks { get; }
    public IReadOnlyList<MaterialLinkRecord> MaterialLinks { get; }
    public SkeletonIdentityV1? SkeletonIdentity { get; }
    public IReadOnlyList<LodMaterialTopologySnapshot> LodMaterialTopologies { get; }
    public TextureResourceSet? TextureResourceSet { get; }
    public int MaterialExtensionVersion { get; }
    public int SkeletonExtensionVersion { get; }
    public ProducerTopologySnapshot? TopologySnapshot { get; }
}

public sealed record ProducerResult
{
    public ProducerResult(IEnumerable<UeFormatExtensionChunk> extensionChunks, IEnumerable<ProtocolDiagnostic> diagnostics)
    {
        ExtensionChunks = Array.AsReadOnly((extensionChunks ?? throw new ArgumentNullException(nameof(extensionChunks)))
            .Select(chunk => chunk is null ? throw new ArgumentException("Extension chunks cannot contain null entries.", nameof(extensionChunks)) : new UeFormatExtensionChunk(chunk.Id, chunk.Count, chunk.Payload)).ToArray());
        Diagnostics = Array.AsReadOnly((diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray());
    }

    public IReadOnlyList<UeFormatExtensionChunk> ExtensionChunks { get; }
    public IReadOnlyList<ProtocolDiagnostic> Diagnostics { get; }
    public bool Success => Diagnostics.All(diagnostic => !string.Equals(diagnostic.Severity, "error", StringComparison.Ordinal));
}

internal sealed class ProducerTopologyValidationException(string code) : Exception
{
    public string Code { get; } = code;
}

public abstract class ProducerAdapter
{
    private static readonly Regex OpaqueFutureStandardIdPattern = new("\\AFUTURE_[A-Z0-9_]+\\z", RegexOptions.CultureInvariant);
    private static readonly IReadOnlyDictionary<ProducerKind, string[]> AllowedStandardIds = new Dictionary<ProducerKind, string[]>
    {
        [ProducerKind.Uemodel] = ["LODS", "SKELETON", "COLLISION"],
        [ProducerKind.Ueanim] = ["TRACKS", "CURVES"],
        [ProducerKind.Uepose] = ["POSES"]
    };

    protected ProducerAdapter(ProducerKind kind) => Kind = kind;
    public ProducerKind Kind { get; }

    public ProducerResult Produce(ProducerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var validationDiagnostics = Validate(snapshot);
        if (validationDiagnostics.Count > 0) return new ProducerResult([], validationDiagnostics);

        var extensionChunks = new List<UeFormatExtensionChunk>();
        var diagnostics = new List<ProtocolDiagnostic>();

        foreach (var writer in GetExtensionWriters(snapshot))
        {
            try
            {
                var chunk = writer.Write();
                if (chunk is not null)
                    extensionChunks.Add(chunk);
            }
            catch (OperationCanceledException) { throw; }
            catch (NotSupportedException)
            {
                diagnostics.Add(UnsupportedExtensionPayload(writer.Subject));
            }
            catch (ProducerTopologyValidationException exception)
            {
                diagnostics.Add(Error(exception.Code, "Standard topology validation failed; no topology payload was emitted and later extensions continued.", writer.Subject));
            }
            catch (InvalidDataException) when (writer.Subject is "LODS" or "SKELETON")
            {
                diagnostics.Add(Error("producer.invalid-standard-topology", "Standard topology validation failed; no topology payload was emitted and later extensions continued.", writer.Subject));
            }
            catch (Exception)
            {
                diagnostics.Add(InvalidExtensionPayload(writer.Subject));
            }
        }

        return new ProducerResult(extensionChunks, ProtocolValidation.Order(diagnostics));
    }

    protected abstract string SkeletonExtensionId { get; }
    protected virtual IEnumerable<(string Subject, Func<UeFormatExtensionChunk?> Write)> GetExtensionWriters(ProducerSnapshot snapshot)
    {
        if (snapshot.MaterialLinks.Count > 0)
            yield return ("FMODEL_MATERIALS", () => throw new NotSupportedException());
        if (snapshot.SkeletonIdentity is not null)
            yield return (SkeletonExtensionId, () => throw new NotSupportedException());
    }

    private IReadOnlyList<ProtocolDiagnostic> Validate(ProducerSnapshot snapshot)
    {
        var diagnostics = new List<ProtocolDiagnostic>();
        if (snapshot.Kind != Kind) diagnostics.Add(Error("producer.kind-mismatch", "The snapshot kind must match the selected producer.", snapshot.Kind.ToString()));
        var allowed = AllowedStandardIds[Kind];
        var required = allowed[0];
        foreach (var chunk in snapshot.StandardChunks.Where(chunk => string.IsNullOrWhiteSpace(chunk.ChunkId)))
            diagnostics.Add(Error("producer.invalid-standard-chunk-id", "Standard chunk identifiers must be non-empty and non-whitespace.", chunk.ChunkId));
        foreach (var extension in snapshot.StandardChunks.Where(chunk => chunk.ChunkId.StartsWith("FMODEL_", StringComparison.Ordinal)))
            diagnostics.Add(Error("producer.extension-id-in-standard", "Extension chunk identifiers cannot be supplied as standard chunks.", extension.ChunkId));
        foreach (var chunk in snapshot.StandardChunks.Where(chunk => !chunk.ChunkId.StartsWith("FMODEL_", StringComparison.Ordinal) && allowed.Contains(chunk.ChunkId, StringComparer.Ordinal) == false && IsKnownStandard(chunk.ChunkId)))
            diagnostics.Add(Error("producer.invalid-standard-chunk", "The standard chunk is not supported for this producer kind.", chunk.ChunkId));
        foreach (var chunk in snapshot.StandardChunks.Where(chunk => !string.IsNullOrWhiteSpace(chunk.ChunkId) && !chunk.ChunkId.StartsWith("FMODEL_", StringComparison.Ordinal) && !IsKnownStandard(chunk.ChunkId) && !OpaqueFutureStandardIdPattern.IsMatch(chunk.ChunkId)))
            diagnostics.Add(Error("producer.invalid-opaque-standard-chunk-id", "Opaque standard chunk identifiers must use the FUTURE_[A-Z0-9_]+ namespace.", chunk.ChunkId));
        foreach (var group in snapshot.StandardChunks.GroupBy(chunk => chunk.ChunkId, StringComparer.Ordinal).Where(group => group.Count() > 1))
            diagnostics.Add(Error("producer.duplicate-standard-chunk", "Standard chunk identifiers may occur only once.", group.Key));
        if (!snapshot.StandardChunks.Any(chunk => string.Equals(chunk.ChunkId, required, StringComparison.Ordinal)))
            diagnostics.Add(Error("producer.missing-required-standard-chunk", "The required standard chunk must occur exactly once.", required));
        if (snapshot.MaterialLinks.Count > 0 && Kind != ProducerKind.Uemodel)
            diagnostics.Add(Error("producer.material-links-unsupported", "Material-link extensions are supported only for UEMODEL.", Kind.ToString()));
        if (snapshot.MaterialLinks.Count > 0 && snapshot.MaterialExtensionVersion != ProducerSnapshot.CurrentExtensionVersion)
            diagnostics.Add(Error("producer.invalid-extension-version", "Material extension version is unsupported.", "FMODEL_MATERIALS"));
        if (snapshot.TextureResourceSet is not null)
        {
            if (Kind != ProducerKind.Uemodel)
                diagnostics.Add(Error("producer.texture-resources-unsupported", "Texture extensions are supported only for UEMODEL.", Kind.ToString()));
            if (snapshot.MaterialExtensionVersion != ProducerSnapshot.CurrentExtensionVersion)
                diagnostics.Add(Error("producer.invalid-extension-version", "Texture extension version is unsupported.", "FMODEL_TEXTURES"));
        }
        if (snapshot.SkeletonIdentity is not null && snapshot.SkeletonExtensionVersion != ProducerSnapshot.CurrentExtensionVersion)
            diagnostics.Add(Error("producer.invalid-extension-version", "Skeleton extension version is unsupported.", SkeletonExtensionId));
        if (Kind == ProducerKind.Uemodel && snapshot.SkeletonIdentity is not null && !snapshot.StandardChunks.Any(chunk => string.Equals(chunk.ChunkId, "SKELETON", StringComparison.Ordinal)))
            diagnostics.Add(Error("producer.missing-skeleton-standard-chunk", "Skeleton identity extensions require the opaque standard SKELETON chunk.", "SKELETON"));
        foreach (var material in snapshot.MaterialLinks.Where(link => string.IsNullOrWhiteSpace(link.MaterialId)))
            diagnostics.Add(Error("producer.invalid-material-id", "Material link identifiers must be non-empty and non-whitespace.", material.MaterialId));
        foreach (var group in snapshot.MaterialLinks.GroupBy(link => link.MaterialId, StringComparer.Ordinal).Where(group => group.Count() > 1))
            diagnostics.Add(Error("producer.duplicate-material-id", "Material link identifiers must be unique.", group.Key));
        foreach (var material in snapshot.MaterialLinks)
            diagnostics.AddRange(ProtocolValidation.Validate(material));
        if (snapshot.SkeletonIdentity is not null)
            diagnostics.AddRange(ProtocolValidation.Validate(snapshot.SkeletonIdentity));
        if (Kind == ProducerKind.Uemodel)
        {
            ValidateTopologyIndices(snapshot, diagnostics);
            if (snapshot.MaterialLinks.Count > 0)
                ValidateTopology(snapshot, diagnostics);
        }
        return ProtocolValidation.Order(diagnostics);
    }

    private static bool IsKnownStandard(string chunkId) => AllowedStandardIds.Values.SelectMany(ids => ids).Contains(chunkId, StringComparer.Ordinal);

    private static void ValidateTopology(ProducerSnapshot snapshot, ICollection<ProtocolDiagnostic> diagnostics)
    {
        var expected = snapshot.MaterialLinks.Select(link => link.MaterialId).ToArray();
        if (snapshot.LodMaterialTopologies.Count == 0)
        {
            diagnostics.Add(Error("producer.missing-lod-material-topology", "UEMODEL material links require topology slots for every supplied LOD.", "LODS"));
            return;
        }
        foreach (var topology in snapshot.LodMaterialTopologies)
        {
            if (topology.LodIndex < 0 || !topology.MaterialIds.SequenceEqual(expected, StringComparer.Ordinal))
                diagnostics.Add(Error("producer.lod-material-topology-mismatch", "LOD material slots must exactly match the material-link order; fallback is a UEScene policy.", topology.LodIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    private static void ValidateTopologyIndices(ProducerSnapshot snapshot, ICollection<ProtocolDiagnostic> diagnostics)
    {
        foreach (var topology in snapshot.LodMaterialTopologies.Where(topology => topology.LodIndex < 0))
            diagnostics.Add(Error("producer.invalid-lod-material-topology", "LOD material topology indices must be non-negative.", "LODS"));
        foreach (var group in snapshot.LodMaterialTopologies.GroupBy(topology => topology.LodIndex).Where(group => group.Count() > 1))
            diagnostics.Add(Error("producer.duplicate-lod-material-topology", "LOD material topology indices must be unique.", group.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static ProtocolDiagnostic Error(string code, string message, string? subject) => new(code, "error", message, subject);
    private static ProtocolDiagnostic InvalidExtensionPayload(string subject) =>
        Error("producer.invalid-extension-payload", "Producer extension payload was invalid; no partial chunk was emitted and later extensions continued.", subject);
    private static ProtocolDiagnostic UnsupportedExtensionPayload(string subject) =>
        Error("producer.extension-payload-unsupported", "Task B prerequisite: a real extension writer is required; synthetic producer payload encoding is test-support only.", subject);
}

public static class ProducerAdapters
{
    public static ProducerAdapter For(ProducerKind kind) => kind switch
    {
        ProducerKind.Uemodel => new UemodelProducerAdapter(),
        ProducerKind.Ueanim => new UeanimProducerAdapter(),
        ProducerKind.Uepose => new UeposeProducerAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
