using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CUE4Parse.UeFormat.Protocol;

public sealed record UeSceneRequest(
    [property: JsonPropertyOrder(1)] string SourceIdentifier,
    [property: JsonPropertyOrder(2)] string RelativeOutputPath,
    [property: JsonPropertyOrder(3)] string LodPolicy,
    [property: JsonPropertyOrder(4)] string DiagnosticsPolicy);
public sealed record UeSceneResource(
    [property: JsonPropertyOrder(1)] string Id,
    [property: JsonPropertyOrder(2)] string Path,
    [property: JsonPropertyOrder(3)] long Bytes,
    [property: JsonPropertyOrder(4)] string Sha256);
public sealed record UeSceneDiagnostic(
    [property: JsonPropertyOrder(1)] string Code,
    [property: JsonPropertyOrder(2)] string Severity,
    [property: JsonPropertyOrder(3)] string Message);
public sealed record UeSceneRecord
{
    [JsonConstructor]
    public UeSceneRecord(string format, bool binaryParse, string sourceIdentifier, int selectedLod, int requestedLod, IReadOnlyList<int> availableLods, IReadOnlyList<UeSceneResource> resources, IReadOnlyList<UeSceneDiagnostic> diagnostics)
    {
        Format = format;
        BinaryParse = binaryParse;
        SourceIdentifier = sourceIdentifier;
        SelectedLod = selectedLod;
        RequestedLod = requestedLod;
        AvailableLods = Array.AsReadOnly((availableLods ?? throw new ArgumentNullException(nameof(availableLods))).ToArray());
        Resources = Array.AsReadOnly((resources ?? throw new ArgumentNullException(nameof(resources))).ToArray());
        Diagnostics = Array.AsReadOnly((diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray());
    }
    [JsonPropertyOrder(1)] public string Format { get; }
    [JsonPropertyOrder(2)] public bool BinaryParse { get; }
    [JsonPropertyOrder(3)] public string SourceIdentifier { get; }
    [JsonPropertyOrder(4)] public int SelectedLod { get; }
    [JsonPropertyOrder(5)] public int RequestedLod { get; }
    [JsonPropertyOrder(6)] public IReadOnlyList<int> AvailableLods { get; }
    [JsonPropertyOrder(7)] public IReadOnlyList<UeSceneResource> Resources { get; }
    [JsonPropertyOrder(8)] public IReadOnlyList<UeSceneDiagnostic> Diagnostics { get; }
}
public sealed record UeSceneReadResult
{
    public UeSceneReadResult(UeSceneRecord? scene, IReadOnlyList<ProtocolDiagnostic> diagnostics)
    {
        Scene = scene;
        Diagnostics = Array.AsReadOnly((diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray());
    }

    public UeSceneRecord? Scene { get; }
    public IReadOnlyList<ProtocolDiagnostic> Diagnostics { get; }
}

public sealed record UeSceneResult
{
    public UeSceneResult(bool success, UeSceneRecord? scene, IReadOnlyList<ProtocolDiagnostic> diagnostics)
    {
        Success = success;
        Scene = scene;
        Diagnostics = Array.AsReadOnly((diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray());
    }

    [JsonPropertyOrder(1)] public bool Success { get; }
    [JsonPropertyOrder(2)] public UeSceneRecord? Scene { get; }
    [JsonPropertyOrder(3)] public IReadOnlyList<ProtocolDiagnostic> Diagnostics { get; }
}
public static class UeSceneJson
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static string Serialize(UeSceneRecord scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return JsonSerializer.Serialize(scene, ProtocolJson.Options);
    }
    public static UeSceneReadResult Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return InvalidJson();
        try
        {
            using var document = JsonDocument.Parse(json);
            RejectDuplicateProperties(document.RootElement);
            var scene = JsonSerializer.Deserialize<UeSceneRecord>(json, ProtocolJson.Options);
            if (scene is null) return InvalidJson();
            var diagnostics = Validate(scene);
            return diagnostics.Count == 0 ? new(scene, []) : new(null, diagnostics);
        }
        catch (JsonException) { return InvalidJson(); }
        catch (ArgumentException) { return InvalidJson(); }
    }
    public static UeSceneReadResult Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 4 && bytes[..4].SequenceEqual("UESE"u8)) return new(null, [new("uescene.binary-blocked", "blocked", "UEScene binary parsing is intentionally unsupported.")]);
        try { return Deserialize(StrictUtf8.GetString(bytes)); }
        catch (DecoderFallbackException) { return InvalidJson(); }
    }

    private static UeSceneReadResult InvalidJson() => new(null, [new("uescene.invalid-json", "error", "UEScene JSON must match the data contract.")]);

    private static void RejectDuplicateProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new JsonException("Duplicate JSON property.");
                    RejectDuplicateProperties(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
                break;
        }
    }

    private static IReadOnlyList<ProtocolDiagnostic> Validate(UeSceneRecord scene)
    {
        var diagnostics = new List<ProtocolDiagnostic>();
        if (scene.Format != "uescene-v1") diagnostics.Add(Error("uescene.invalid-format", "Format must be exactly uescene-v1.", scene.SourceIdentifier));
        if (scene.BinaryParse) diagnostics.Add(Error("uescene.binary-parse-blocked", "UEScene binary parsing must remain disabled.", scene.SourceIdentifier));
        if (!ProtocolValidation.IsSafeRelativePath(scene.SourceIdentifier)) diagnostics.Add(Error("uescene.invalid-source-identifier", "Source identifier must be a non-empty safe relative path.", scene.SourceIdentifier));
        if (scene.SelectedLod < 0) diagnostics.Add(Error("uescene.invalid-selected-lod", "Selected LOD must be non-negative.", scene.SourceIdentifier));
        if (scene.RequestedLod < 0) diagnostics.Add(Error("uescene.invalid-requested-lod", "Requested LOD must be non-negative.", scene.SourceIdentifier));
        if (scene.AvailableLods.Any(lod => lod < 0) || scene.AvailableLods.Distinct().Count() != scene.AvailableLods.Count)
            diagnostics.Add(Error("uescene.invalid-available-lod", "Available LOD values must be unique non-negative integers.", scene.SourceIdentifier));
        if (!scene.AvailableLods.Contains(scene.SelectedLod)) diagnostics.Add(Error("uescene.selected-lod-unavailable", "Selected LOD must be present in available LODs.", scene.SourceIdentifier));
        var validResources = scene.Resources.Where(resource => resource is not null).ToArray();
        var resourcesWithAsciiSafePaths = validResources.Where(resource => ProtocolValidation.IsAsciiSafeRelativePath(resource.Path)).ToArray();
        foreach (var duplicate in validResources.GroupBy(resource => resource.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
            diagnostics.Add(Error("uescene.duplicate-resource-id", "Resource identifiers must be unique.", duplicate.Key));
        foreach (var duplicate in resourcesWithAsciiSafePaths.GroupBy(resource => resource.Path, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            diagnostics.Add(Error("uescene.duplicate-resource-path", "Resource paths must be unique.", duplicate.Key));
        foreach (var resource in scene.Resources)
        {
            if (resource is null)
            {
                diagnostics.Add(Error("uescene.invalid-resource", "Resources must be objects with valid fields.", scene.SourceIdentifier));
                continue;
            }
            if (string.IsNullOrWhiteSpace(resource.Id)) diagnostics.Add(Error("uescene.invalid-resource-id", "Resource identifiers must be non-empty.", resource.Id));
            if (!ProtocolValidation.IsAsciiSafeRelativePath(resource.Path)) diagnostics.Add(Error("uescene.invalid-resource-path", "Resource paths must use ASCII-safe relative-path characters before canonical uniqueness comparison.", resource.Path));
            if (resource.Bytes < 0) diagnostics.Add(Error("uescene.invalid-resource-bytes", "Resource byte counts must be non-negative.", resource.Id));
            if (!ProtocolValidation.IsSha256(resource.Sha256)) diagnostics.Add(Error("uescene.invalid-resource-sha256", "Resource hashes must be SHA-256 hex strings.", resource.Id));
        }
        foreach (var diagnostic in scene.Diagnostics)
            if (diagnostic is null || string.IsNullOrWhiteSpace(diagnostic.Code) || string.IsNullOrWhiteSpace(diagnostic.Message) || diagnostic.Severity is not ("info" or "warning" or "error" or "blocked"))
                diagnostics.Add(Error("uescene.invalid-diagnostic", "Diagnostics must have a code, message, and supported severity.", scene.SourceIdentifier));
        return ProtocolValidation.Order(diagnostics);
    }

    private static ProtocolDiagnostic Error(string code, string message, string? subject) => new(code, "error", message, subject);
}
