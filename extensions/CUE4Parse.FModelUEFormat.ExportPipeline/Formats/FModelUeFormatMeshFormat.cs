using CUE4Parse_Conversion;
using CUE4Parse.FModelUEFormat.ExportPipeline.Diagnostics;
using CUE4Parse.FModelUEFormat.ExportPipeline.Materials;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.FModelUEFormat.ExportPipeline.Writers;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Formats.Meshes;
using CUE4Parse_Conversion.Options;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.Formats;

/// <summary>
/// Standard-preserving UEFormat decorator. It appends V3 chunks only after upstream
/// UEModel serialization; TextureExporter remains the source of output bytes and paths.
/// </summary>
public sealed class FModelUeFormatMeshFormat : IMeshExportFormat
{
    private readonly UEFormatMeshFormat upstream = new();
    private readonly FModelUeFormatPolicy policy;
    private readonly Action<FModelUeFormatPipelineDiagnostic>? diagnosticSink;
    private readonly ExportSession? session;
    private readonly UObject? sourceObject;
    private readonly FModelMaterialProjectionBuilder projectionBuilder = new();
    private readonly FModelUeModelExtensionComposer extensionComposer = new();

    public FModelUeFormatMeshFormat(
        FModelUeFormatPolicy policy,
        Action<FModelUeFormatPipelineDiagnostic>? diagnosticSink = null)
    {
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
        this.diagnosticSink = diagnosticSink;
    }

    public FModelUeFormatMeshFormat(
        FModelUeFormatPolicy policy,
        ExportSession session,
        UObject sourceObject,
        Action<FModelUeFormatPipelineDiagnostic>? diagnosticSink = null)
        : this(policy, diagnosticSink)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.sourceObject = sourceObject ?? throw new ArgumentNullException(nameof(sourceObject));
    }

    public string DisplayName => "UEFormat (V3 adapter)";

    public IReadOnlyList<ExportFile> BuildStaticMesh(string objectName, ExportOptions options, StaticMeshDto dto, IReadOnlyDictionary<string, string>? materialPaths = null)
    {
        var context = CreateBoundContext(dto, objectName);
        return BuildStaticMesh(objectName, context.ObjectPath, options, dto, materialPaths, context.TexturePreview, context.OwnerModelUri);
    }

    public IReadOnlyList<ExportFile> BuildStaticMesh(
        string objectName,
        ExportOptions options,
        StaticMeshDto dto,
        IReadOnlyDictionary<string, string>? materialPaths,
        FModelTextureExportPreview? texturePreview,
        string? ownerModelUri = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectName);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dto);
        return BuildStaticMesh(objectName, BuildObjectPath(dto, objectName), options, dto, materialPaths, texturePreview, ownerModelUri);
    }

    public IReadOnlyList<ExportFile> BuildStaticMesh(
        string objectName,
        string objectPath,
        ExportOptions options,
        StaticMeshDto dto,
        IReadOnlyDictionary<string, string>? materialPaths = null,
        FModelTextureExportPreview? texturePreview = null,
        string? ownerModelUri = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectPath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dto);

        var upstreamFiles = upstream.BuildStaticMesh(objectName, options, dto, materialPaths);
        return AppendMaterialProjection(
            objectPath,
            "static mesh",
            upstreamFiles,
            dto.Materials.Length,
            dto.LODs.Count == 0 ? 0 : dto.LODs[0].Sections.Length,
            () => projectionBuilder.BuildFromStaticMeshDto(objectPath, dto, options, policy, texturePreview, ownerModelUri));
    }

    public IReadOnlyList<ExportFile> BuildSkeletalMesh(string objectName, ExportOptions options, SkeletalMeshDto dto, IReadOnlyDictionary<string, string>? materialPaths = null)
    {
        var context = CreateBoundContext(dto, objectName);
        return BuildSkeletalMesh(objectName, context.ObjectPath, options, dto, materialPaths, context.TexturePreview, context.OwnerModelUri);
    }

    public IReadOnlyList<ExportFile> BuildSkeletalMesh(
        string objectName,
        ExportOptions options,
        SkeletalMeshDto dto,
        IReadOnlyDictionary<string, string>? materialPaths,
        FModelTextureExportPreview? texturePreview,
        string? ownerModelUri = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectName);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dto);
        return BuildSkeletalMesh(objectName, BuildObjectPath(dto, objectName), options, dto, materialPaths, texturePreview, ownerModelUri);
    }

    public IReadOnlyList<ExportFile> BuildSkeletalMesh(
        string objectName,
        string objectPath,
        ExportOptions options,
        SkeletalMeshDto dto,
        IReadOnlyDictionary<string, string>? materialPaths = null,
        FModelTextureExportPreview? texturePreview = null,
        string? ownerModelUri = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectPath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dto);

        var upstreamFiles = upstream.BuildSkeletalMesh(objectName, options, dto, materialPaths);
        return AppendMaterialProjection(
            objectPath,
            "skeletal mesh",
            upstreamFiles,
            dto.Materials.Length,
            dto.LODs.Count == 0 ? 0 : dto.LODs[0].Sections.Length,
            () => projectionBuilder.BuildFromSkeletalMeshDto(objectPath, dto, options, policy, texturePreview, ownerModelUri));
    }

    public IReadOnlyList<ExportFile> BuildSkeleton(string objectName, ExportOptions options, SkeletonDto dto)
        => upstream.BuildSkeleton(objectName, options, dto);

    private BoundContext CreateBoundContext(ObjectDto dto, string objectName)
    {
        if (session is null || sourceObject is null)
            return new BoundContext(BuildObjectPath(dto, objectName), null, null);

        var ownerUriRoot = policy.ResourceUriRoot == FModelUeFormatResourceUriRoot.RelativeToOwner
            ? FModelUeFormatResourceUriRoot.ExportRoot
            : policy.ResourceUriRoot;
        var ownerModelUri = FModelMaterialUriResolver.ResolveResourceUri(
            sourceObject,
            "uemodel",
            resourceUriRoot: ownerUriRoot);
        return new BoundContext(
            sourceObject.GetPathName(),
            new FModelTextureExportPreview(session, policy.ResourceUriRoot, ownerModelUri),
            ownerModelUri);
    }

    private IReadOnlyList<ExportFile> AppendMaterialProjection(
        string objectPath,
        string meshKind,
        IReadOnlyList<ExportFile> upstreamFiles,
        int sourceMaterialCount,
        int requiredSectionCount,
        Func<FModelMaterialProjection> projectionFactory)
    {
        if (!policy.Enabled || policy.MaterialMode == FModelUeFormatMaterialMode.Disabled)
            return upstreamFiles;
        if (upstreamFiles.Count != 1 || !string.Equals(upstreamFiles[0].Extension, "uemodel", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Upstream UEFormat {meshKind} serialization must return exactly one .uemodel file.");

        var projection = projectionFactory();
        Publish(projection.Diagnostics);
        if (projection.Materials.Count == 0)
        {
            if (policy.RequiresCompleteMaterialProjection)
                throw new InvalidOperationException("Strict material projection produced no material links.");
            PublishFallback(objectPath, "material.projection.empty", "V3 material links were omitted because no complete material projection was available.");
            return upstreamFiles;
        }
        if (projection.Materials.Count != requiredSectionCount)
        {
            if (policy.RequiresCompleteMaterialProjection)
                throw new InvalidOperationException("Strict material projection does not cover every standard LOD0 material section.");
            PublishFallback(objectPath, "material.projection.incomplete", "V3 material links were omitted because they did not cover every standard LOD0 material section.");
            return upstreamFiles;
        }

        try
        {
            var bytes = extensionComposer.AppendMaterialLinks(upstreamFiles[0].Data, projection, sourceMaterialCount);
            return [new ExportFile("uemodel", bytes)];
        }
        catch when (!policy.RequiresCompleteMaterialProjection)
        {
            // Linked mode keeps the upstream standard output if optional V3 projection
            // cannot be validated. It never emits a partial extension chunk.
            PublishFallback(objectPath, "material.extension.fallback", "V3 material extension validation failed; standard UEFormat output was preserved.");
            return upstreamFiles;
        }
    }

    private void Publish(IEnumerable<FModelUeFormatPipelineDiagnostic> diagnostics)
    {
        if (diagnosticSink is null)
            return;
        foreach (var diagnostic in diagnostics)
            diagnosticSink(diagnostic);
    }

    private void PublishFallback(string objectPath, string code, string message)
    {
        diagnosticSink?.Invoke(new FModelUeFormatPipelineDiagnostic(
            code,
            FModelUeFormatDiagnosticSeverity.Warning,
            FModelUeFormatPipelineStage.ExtensionSerialization,
            objectPath,
            message,
            recovery: "Use strict material mode to turn this fallback into a hard export failure."));
    }

    private static string BuildObjectPath(ObjectDto dto, string objectName) =>
        string.IsNullOrWhiteSpace(dto.Path) ? objectName : dto.Path + "." + objectName;

    private readonly record struct BoundContext(
        string ObjectPath,
        FModelTextureExportPreview? TexturePreview,
        string? OwnerModelUri);
}