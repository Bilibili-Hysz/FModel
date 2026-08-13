namespace CUE4Parse.UeFormat.Protocol;

/// <summary>Appends extension-owned chunks after a standard UEFormat writer completes its work.</summary>
public interface IUeFormatChunkExtension
{
    void WriteChunks(UeFormatChunkContext context, IArchiveWriter writer);
}

/// <summary>A minimal, vendor-neutral output seam for extension chunks.</summary>
public interface IArchiveWriter
{
    void WriteChunk(UeFormatExtensionChunk chunk);
}

/// <summary>Bounded result of invoking append-only post-chunk extensions.</summary>
public sealed record UeFormatExtensionInvocationResult(IReadOnlyList<UeFormatExtensionDiagnostic> Diagnostics)
{
    public static UeFormatExtensionInvocationResult Empty { get; } = new(Array.Empty<UeFormatExtensionDiagnostic>());
}

public sealed record UeFormatExtensionDiagnostic(string Code, string Severity, string Message, string ExtensionId);

public enum UeFormatExtensionFailurePolicy
{
    Optional,
    Required
}

/// <summary>Sanitized failure raised when a required post-chunk extension cannot complete.</summary>
public sealed class UeFormatRequiredExtensionException : Exception
{
    public UeFormatRequiredExtensionException(string extensionId)
        : base($"Required UEFormat post-chunk extension '{RequireId(extensionId)}' failed.")
    {
        ExtensionId = extensionId;
    }

    public string ExtensionId { get; }

    private static string RequireId(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId)) throw new ArgumentException("An extension identifier is required.", nameof(extensionId));
        return extensionId;
    }
}

/// <summary>
/// Aggregate-side registration seam for a standard writer's post-chunk callback.
/// This owns no wire-format behavior: it receives a read-only context and can only append through the supplied writer.
/// Extension chunks are committed atomically after every required extension succeeds.
/// </summary>
public sealed class UeFormatPostChunkExtensionRegistry
{
    private readonly SortedDictionary<string, Registration> extensions = new(StringComparer.Ordinal);

    public IReadOnlyList<string> RegisteredIds => extensions.Keys.ToArray();

    public void Register(string id, IUeFormatChunkExtension extension) =>
        Register(id, extension, UeFormatExtensionFailurePolicy.Optional);

    public void Register(string id, IUeFormatChunkExtension extension, UeFormatExtensionFailurePolicy failurePolicy)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An extension identifier is required.", nameof(id));
        ArgumentNullException.ThrowIfNull(extension);
        if (!Enum.IsDefined(failurePolicy)) throw new ArgumentOutOfRangeException(nameof(failurePolicy));
        if (!extensions.TryAdd(id, new(extension, failurePolicy))) throw new ArgumentException($"An extension is already registered for '{id}'.", nameof(id));
    }

    public UeFormatExtensionInvocationResult AppendExtensions(UeFormatChunkContext context, IArchiveWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);
        if (extensions.Count == 0) return UeFormatExtensionInvocationResult.Empty;

        var diagnostics = new List<UeFormatExtensionDiagnostic>();
        var aggregateWriter = new BufferingArchiveWriter();
        foreach (var (id, registration) in extensions)
        {
            try
            {
                var extensionWriter = new BufferingArchiveWriter();
                registration.Extension.WriteChunks(context, extensionWriter);
                extensionWriter.FlushTo(aggregateWriter);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                if (registration.FailurePolicy == UeFormatExtensionFailurePolicy.Required)
                    throw new UeFormatRequiredExtensionException(id);

                diagnostics.Add(new("ueformat.extension-failed", "warning", "An optional post-chunk extension failed; standard output was preserved.", id));
            }
        }

        aggregateWriter.FlushTo(writer);
        return new UeFormatExtensionInvocationResult(Array.AsReadOnly(diagnostics.ToArray()));
    }

    private sealed record Registration(IUeFormatChunkExtension Extension, UeFormatExtensionFailurePolicy FailurePolicy);

    private sealed class BufferingArchiveWriter : IArchiveWriter
    {
        private readonly List<UeFormatExtensionChunk> chunks = [];

        public void WriteChunk(UeFormatExtensionChunk chunk)
        {
            ArgumentNullException.ThrowIfNull(chunk);
            chunks.Add(new UeFormatExtensionChunk(chunk.Id, chunk.Count, chunk.Payload));
        }

        public void FlushTo(IArchiveWriter writer)
        {
            foreach (var chunk in chunks)
            {
                writer.WriteChunk(chunk);
            }
        }
    }
}

public sealed record UeFormatLodInfo(int LodIndex, int VertexCount, int TriangleCount);

/// <summary>Read-only producer metadata with no dependency on concrete asset types.</summary>
public sealed record UeFormatChunkContext
{
    public UeFormatChunkContext(string sourceIdentifier, string exportName, string exportKind, IReadOnlyList<UeFormatLodInfo> lods)
    {
        SourceIdentifier = sourceIdentifier;
        ExportName = exportName;
        ExportKind = exportKind;
        Lods = Array.AsReadOnly((lods ?? throw new ArgumentNullException(nameof(lods))).ToArray());
    }

    public string SourceIdentifier { get; }
    public string ExportName { get; }
    public string ExportKind { get; }
    public IReadOnlyList<UeFormatLodInfo> Lods { get; }
}

/// <summary>Stage 1 standalone scene-export contract, expressed only in vendor-neutral boundary data.</summary>
public interface IUeSceneExportService
{
    bool CanExport(UeSceneSource source);
    SceneExportResult Export(UeSceneSource source, UeSceneExportRequest request, CancellationToken cancellationToken);
}

/// <summary>Stage 1 scene identity; it is not an alias for a vendor UObject or UWorld.</summary>
public sealed record UeSceneSource
{
    public UeSceneSource(string identifier, string sourceKind, IReadOnlyDictionary<string, string> metadata)
    {
        Identifier = identifier;
        SourceKind = sourceKind;
        Metadata = Freeze(metadata);
    }

    public string Identifier { get; }
    public string SourceKind { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    private static IReadOnlyDictionary<string, string> Freeze(IReadOnlyDictionary<string, string> values) =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(values ?? throw new ArgumentNullException(nameof(values)), StringComparer.Ordinal));
}

public sealed record UeFormatSettingsSnapshot(string MaterialLinkMode, string MaterialExportLayout);

public enum UeSceneExportStrategy
{
    Scene,
    SelfContained
}

public enum UeSceneLodPolicy
{
    Selected,
    Highest,
    Lod0
}

public enum UeSceneDiagnosticsPolicy
{
    Default,
    Strict,
    CollectAll
}

/// <summary>Strict codec for the stable lowercase policy tokens used at host and serialization boundaries.</summary>
public static class UeScenePolicyCodec
{
    public static UeSceneExportStrategy ParseExportStrategy(string? value) => value switch
    {
        "scene" => UeSceneExportStrategy.Scene,
        "self-contained" => UeSceneExportStrategy.SelfContained,
        _ => throw new ArgumentException("Unsupported scene export strategy wire value.", nameof(value))
    };

    public static UeSceneLodPolicy ParseLodPolicy(string? value) => value switch
    {
        "selected" => UeSceneLodPolicy.Selected,
        "highest" => UeSceneLodPolicy.Highest,
        "lod0" => UeSceneLodPolicy.Lod0,
        _ => throw new ArgumentException("Unsupported scene LOD policy wire value.", nameof(value))
    };

    public static UeSceneDiagnosticsPolicy ParseDiagnosticsPolicy(string? value) => value switch
    {
        "default" => UeSceneDiagnosticsPolicy.Default,
        "strict" => UeSceneDiagnosticsPolicy.Strict,
        "collect-all" => UeSceneDiagnosticsPolicy.CollectAll,
        _ => throw new ArgumentException("Unsupported scene diagnostics policy wire value.", nameof(value))
    };

    public static string ToWireValue(this UeSceneExportStrategy value) => value switch
    {
        UeSceneExportStrategy.Scene => "scene",
        UeSceneExportStrategy.SelfContained => "self-contained",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string ToWireValue(this UeSceneLodPolicy value) => value switch
    {
        UeSceneLodPolicy.Selected => "selected",
        UeSceneLodPolicy.Highest => "highest",
        UeSceneLodPolicy.Lod0 => "lod0",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string ToWireValue(this UeSceneDiagnosticsPolicy value) => value switch
    {
        UeSceneDiagnosticsPolicy.Default => "default",
        UeSceneDiagnosticsPolicy.Strict => "strict",
        UeSceneDiagnosticsPolicy.CollectAll => "collect-all",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}

public sealed record UeSceneExportRequest
{
    public UeSceneExportRequest(
        string sourcePath,
        string logicalResourceRoot,
        string physicalOutputRoot,
        UeSceneExportStrategy exportStrategy,
        UeFormatSettingsSnapshot settings,
        UeSceneLodPolicy lodPolicy,
        UeSceneDiagnosticsPolicy diagnosticsPolicy)
    {
        if (!Enum.IsDefined(exportStrategy)) throw new ArgumentOutOfRangeException(nameof(exportStrategy));
        if (!Enum.IsDefined(lodPolicy)) throw new ArgumentOutOfRangeException(nameof(lodPolicy));
        if (!Enum.IsDefined(diagnosticsPolicy)) throw new ArgumentOutOfRangeException(nameof(diagnosticsPolicy));
        SourcePath = sourcePath;
        LogicalResourceRoot = logicalResourceRoot;
        PhysicalOutputRoot = physicalOutputRoot;
        ExportStrategy = exportStrategy;
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        LodPolicy = lodPolicy;
        DiagnosticsPolicy = diagnosticsPolicy;
    }

    public UeSceneExportRequest(
        string sourcePath,
        string logicalResourceRoot,
        string physicalOutputRoot,
        string exportStrategy,
        UeFormatSettingsSnapshot settings,
        string lodPolicy,
        string diagnosticsPolicy)
        : this(
            sourcePath,
            logicalResourceRoot,
            physicalOutputRoot,
            UeScenePolicyCodec.ParseExportStrategy(exportStrategy),
            settings,
            UeScenePolicyCodec.ParseLodPolicy(lodPolicy),
            UeScenePolicyCodec.ParseDiagnosticsPolicy(diagnosticsPolicy))
    {
    }

    public string SourcePath { get; }
    public string LogicalResourceRoot { get; }
    public string PhysicalOutputRoot { get; }
    public UeSceneExportStrategy ExportStrategy { get; }
    public UeFormatSettingsSnapshot Settings { get; }
    public UeSceneLodPolicy LodPolicy { get; }
    public UeSceneDiagnosticsPolicy DiagnosticsPolicy { get; }
}

public sealed record ExportDiagnostic(
    string Code,
    string Severity,
    string Message,
    string? SourceIdentifier,
    string? Recovery);

public sealed record SceneExportResult
{
    public SceneExportResult(bool success, string? scenePath, int resourceCount, int actorCount, int componentCount,
        IReadOnlyList<ExportDiagnostic> diagnostics, string? failureCode, string? recoveryStagePath)
    {
        Success = success;
        ScenePath = scenePath;
        ResourceCount = resourceCount;
        ActorCount = actorCount;
        ComponentCount = componentCount;
        Diagnostics = Array.AsReadOnly((diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))).ToArray());
        FailureCode = failureCode;
        RecoveryStagePath = recoveryStagePath;
    }

    public bool Success { get; }
    public string? ScenePath { get; }
    public int ResourceCount { get; }
    public int ActorCount { get; }
    public int ComponentCount { get; }
    public IReadOnlyList<ExportDiagnostic> Diagnostics { get; }
    public string? FailureCode { get; }
    public string? RecoveryStagePath { get; }
}
