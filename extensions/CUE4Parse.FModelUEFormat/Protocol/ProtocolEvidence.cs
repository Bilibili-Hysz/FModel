using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CUE4Parse.UeFormat.Protocol;

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}

public sealed record ProtocolDiagnostic(
    [property: JsonPropertyOrder(1)] string Code,
    [property: JsonPropertyOrder(2)] string Severity,
    [property: JsonPropertyOrder(3)] string Message,
    [property: JsonPropertyOrder(4)] string? Subject = null);

public sealed record ResourceProvenance(
    [property: JsonPropertyOrder(1)] string Kind,
    [property: JsonPropertyOrder(2)] string Source,
    [property: JsonPropertyOrder(3)] string Revision);

public sealed record ResourceEntry(
    [property: JsonPropertyOrder(1)] string ResourceId,
    [property: JsonPropertyOrder(2)] string RelativePath,
    [property: JsonPropertyOrder(3)] long Bytes,
    [property: JsonPropertyOrder(4)] string Sha256,
    [property: JsonPropertyOrder(5)] ResourceProvenance Provenance);

public sealed record ResourceManifest
{
    public ResourceManifest(string manifestId, IEnumerable<ResourceEntry> resources)
    {
        ManifestId = manifestId;
        Resources = Array.AsReadOnly((resources ?? throw new ArgumentNullException(nameof(resources))).ToArray());
    }

    [JsonPropertyOrder(1)] public string ManifestId { get; }
    [JsonPropertyOrder(2)] public IReadOnlyList<ResourceEntry> Resources { get; }
}

public sealed record ResourceWriteStatus
{
    public ResourceWriteStatus(string resourceId, string status, long bytesWritten, string sha256, IEnumerable<ProtocolDiagnostic> diagnostics)
    {
        ResourceId = resourceId; Status = status; BytesWritten = bytesWritten; Sha256 = sha256;
        Diagnostics = Array.AsReadOnly((diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray());
    }
    [JsonPropertyOrder(1)] public string ResourceId { get; }
    [JsonPropertyOrder(2)] public string Status { get; }
    [JsonPropertyOrder(3)] public long BytesWritten { get; }
    [JsonPropertyOrder(4)] public string Sha256 { get; }
    [JsonPropertyOrder(5)] public IReadOnlyList<ProtocolDiagnostic> Diagnostics { get; }
}

public sealed record UeFormatChunkRecord
{
    [JsonConstructor]
    public UeFormatChunkRecord(string chunkId, int declaredBytes, byte[] jsonPayload)
    {
        ChunkId = chunkId ?? throw new ArgumentNullException(nameof(chunkId));
        DeclaredBytes = declaredBytes;
        _payload = (jsonPayload ?? throw new ArgumentNullException(nameof(jsonPayload))).ToArray();
    }
    private readonly byte[] _payload;
    [JsonPropertyOrder(1)] public string ChunkId { get; }
    [JsonPropertyOrder(2)] public int DeclaredBytes { get; }
    [JsonIgnore] public ReadOnlyMemory<byte> Payload => _payload.ToArray();
    [JsonPropertyOrder(3)]
    [JsonPropertyName("payload")]
    public byte[] JsonPayload => _payload.ToArray();
}

public sealed record UeFormatChunkEnvelope
{
    [JsonConstructor]
    public UeFormatChunkEnvelope(IReadOnlyList<UeFormatChunkRecord> chunks) => Chunks = Array.AsReadOnly((chunks ?? throw new ArgumentNullException(nameof(chunks))).ToArray());
    [JsonPropertyOrder(1)] public IReadOnlyList<UeFormatChunkRecord> Chunks { get; }
}

public static class ProtocolValidation
{
    private static readonly Regex HashPattern = new("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex AsciiSafePathPattern = new("^[A-Za-z0-9._/-]+$", RegexOptions.CultureInvariant);
    private static readonly Regex SecretPattern = new("(?i)(secret|password|token|apikey|aes)", RegexOptions.CultureInvariant);

    public static IReadOnlyList<ProtocolDiagnostic> Validate(ResourceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var diagnostics = new List<ProtocolDiagnostic>();
        foreach (var group in manifest.Resources.GroupBy(resource => resource.ResourceId, StringComparer.Ordinal).Where(group => group.Count() > 1))
            diagnostics.Add(Error("resource.duplicate-id", "Resource identifiers must be unique.", group.Key));
        foreach (var resource in manifest.Resources)
        {
            if (!IsSafeRelativePath(resource.RelativePath)) diagnostics.Add(Error("resource.invalid-relative-path", "Resource paths must be safe relative paths.", resource.RelativePath));
            if (resource.Bytes < 0) diagnostics.Add(Error("resource.invalid-bytes", "Resource byte counts must be non-negative.", resource.ResourceId));
            if (!HashPattern.IsMatch(resource.Sha256 ?? string.Empty)) diagnostics.Add(Error("resource.invalid-sha256", "Resource hashes must be SHA-256 hex strings.", resource.ResourceId));
            if (resource.Provenance is null
                || string.IsNullOrWhiteSpace(resource.Provenance.Kind)
                || string.IsNullOrWhiteSpace(resource.Provenance.Source)
                || string.IsNullOrWhiteSpace(resource.Provenance.Revision)
                || ContainsSecret(resource.Provenance.Kind)
                || ContainsSecret(resource.Provenance.Source)
                || ContainsSecret(resource.Provenance.Revision))
                diagnostics.Add(Error("resource.invalid-provenance", "Resource provenance must be present, complete, and secret-free.", resource.ResourceId));
        }
        return Order(diagnostics);
    }

    public static IReadOnlyList<ProtocolDiagnostic> Validate(MaterialLinkRecord material)
    {
        ArgumentNullException.ThrowIfNull(material);
        var diagnostics = new List<ProtocolDiagnostic>();
        if (!IsSafeMaterialUri(material.MaterialUri))
            diagnostics.Add(Error("material.invalid-uri", "Material links must use safe relative URI paths.", material.MaterialUri));
        if (!IsSafeRelativePath(material.RelativeTexturePath))
            diagnostics.Add(Error("material.invalid-relative-texture-path", "Material texture paths must be safe relative paths.", material.RelativeTexturePath));
        return Order(diagnostics);
    }

    public static IReadOnlyList<ProtocolDiagnostic> Validate(SkeletonIdentityRecord identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return IsSha256(identity.Sha256)
            ? []
            : [Error("skeleton.invalid-sha256", "Skeleton identity hashes must be SHA-256 hex strings.", identity.SkeletonId)];
    }

    public static IReadOnlyList<ProtocolDiagnostic> Validate(SkeletonIdentityV1 identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var diagnostics = new List<ProtocolDiagnostic>();
        try
        {
            SkeletonIdentityChunkWriter.ValidateLogicalUnrealPath(identity.SkeletonPath, nameof(identity.SkeletonPath));
        }
        catch (ArgumentException)
        {
            diagnostics.Add(Error("skeleton.invalid-path", "Skeleton identity paths must be valid logical Unreal paths.", identity.SkeletonPath));
        }

        if (identity.BoneLayoutHash.Length != 32)
            diagnostics.Add(Error("skeleton.invalid-layout-hash", "Skeleton layout hashes must be exactly 32 bytes.", identity.SkeletonPath));

        return Order(diagnostics);
    }

    public static IReadOnlyList<ProtocolDiagnostic> Validate(ResourceWriteStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return IsSha256(status.Sha256)
            ? []
            : [Error("resource-write.invalid-sha256", "Resource write hashes must be SHA-256 hex strings.", status.ResourceId)];
    }

    public static IReadOnlyList<ProtocolDiagnostic> Validate(UeFormatChunkEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var diagnostics = new List<ProtocolDiagnostic>();
        foreach (var chunk in envelope.Chunks)
        {
            if (chunk is null)
            {
                diagnostics.Add(Error("chunk.invalid-chunk", "Chunks must be objects with valid fields.", null));
                continue;
            }
            if (chunk.DeclaredBytes < 0 || chunk.Payload.Length != chunk.DeclaredBytes)
                diagnostics.Add(Error("chunk.invalid-payload-length", "Chunk payload length must exactly equal its declared byte count.", chunk.ChunkId));
        }
        return Order(diagnostics);
    }

    public static bool IsSafeRelativePath(string? path) => !string.IsNullOrWhiteSpace(path)
        && !path.Contains('\\')
        && !Path.IsPathRooted(path) && !path.Replace('\\', '/').StartsWith("/", StringComparison.Ordinal)
        && !Regex.IsMatch(path, "^[A-Za-z]:", RegexOptions.CultureInvariant)
        && path.Replace('\\', '/').Split('/').All(part => part is not "" and not "." and not "..")
        && !ContainsSecret(path);

    public static bool IsAsciiSafeRelativePath(string? path) => IsSafeRelativePath(path)
        && AsciiSafePathPattern.IsMatch(path!);

    public static bool IsSha256(string? hash) => HashPattern.IsMatch(hash ?? string.Empty);
    private static bool IsSafeMaterialUri(string? uri) => IsSafeRelativePath(uri) && !Uri.TryCreate(uri, UriKind.Absolute, out _);
    private static bool ContainsSecret(string? value) => !string.IsNullOrWhiteSpace(value) && SecretPattern.IsMatch(value);
    private static ProtocolDiagnostic Error(string code, string message, string? subject) => new(code, "error", message, subject);
    internal static IReadOnlyList<ProtocolDiagnostic> Order(IEnumerable<ProtocolDiagnostic> diagnostics) => diagnostics.OrderBy(item => item.Code, StringComparer.Ordinal).ThenBy(item => item.Subject, StringComparer.Ordinal).ThenBy(item => item.Message, StringComparer.Ordinal).ToArray();
}
