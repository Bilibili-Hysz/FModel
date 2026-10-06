using CUE4Parse.FModelUEFormat.ExportPipeline.Diagnostics;
using CUE4Parse.FModelUEFormat.ExportPipeline.Delivery;
using CUE4Parse.FModelUEFormat.ExportPipeline.Materials;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.FModelUEFormat.ExportPipeline.Writers;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Writers;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Formats.Meshes;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Writers.UEFormat;
using CUE4Parse_Conversion.Writers.UEFormat.Structs;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.Formats;

/// <summary>
/// UEFormat V10 mesh adapter. Standard attributes remain owned by the official
/// UEModel writer; FModel attributes are inserted through its root-attribute
/// callback before the body is serialized and compressed.
/// </summary>
public sealed class FModelUeFormatMeshFormat : IMeshExportFormat
{
    private readonly FModelUeFormatPolicy policy;
    private readonly Action<FModelUeFormatPipelineDiagnostic>? diagnosticSink;
    private readonly Action<FModelUeFormatDeliveryDeclaration>? deliverySink;
    private readonly ExportSession? session;
    private readonly UObject? sourceObject;
    private readonly FModelMaterialProjectionBuilder projectionBuilder = new();
    private readonly FModelV10AttributeWriter attributeWriter = new();

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
        Action<FModelUeFormatPipelineDiagnostic>? diagnosticSink = null,
        Action<FModelUeFormatDeliveryDeclaration>? deliverySink = null)
        : this(policy, diagnosticSink)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.sourceObject = sourceObject ?? throw new ArgumentNullException(nameof(sourceObject));
        this.deliverySink = deliverySink;
    }

    public string DisplayName => "UEFormat (V10 FModel adapter)";

    public IReadOnlyList<ExportFile> BuildStaticMesh(
        string objectName,
        string objectPath,
        ExportOptions options,
        StaticMeshDto dto,
        IReadOnlyDictionary<string, string>? materialPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectPath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dto);

        var context = CreateBoundContext(objectPath);
        var projection = TryBuildProjection(
            context.ObjectPath,
            "static mesh",
            dto.Materials.Length,
            dto.LODs.Count == 0 ? 0 : dto.LODs[0].Sections.Length,
            HasCompatibleLodMaterialTopology(dto.LODs.Select(lod =>
                (IReadOnlyList<int>)lod.Sections.Select(section => section.MaterialIndex).ToArray())),
            () => projectionBuilder.BuildFromStaticMeshDto(
                context.ObjectPath,
                dto,
                options,
                policy,
                context.TexturePreview,
                context.OwnerModelUri));

        return SaveModel(
            () => new UEModel(
                objectName,
                context.ObjectPath,
                dto,
                options,
                extensionBuilder: BuildExtension(context.ObjectPath, projection, dto.Materials.Length, null, objectPath, context.OwnerModelUri)));
    }

    public IReadOnlyList<ExportFile> BuildSkeletalMesh(
        string objectName,
        string objectPath,
        ExportOptions options,
        SkeletalMeshDto dto,
        IReadOnlyDictionary<string, string>? materialPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectPath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dto);

        var context = CreateBoundContext(objectPath);
        var projection = TryBuildProjection(
            context.ObjectPath,
            "skeletal mesh",
            dto.Materials.Length,
            dto.LODs.Count == 0 ? 0 : dto.LODs[0].Sections.Length,
            HasCompatibleLodMaterialTopology(dto.LODs.Select(lod =>
                (IReadOnlyList<int>)lod.Sections.Select(section => section.MaterialIndex).ToArray())),
            () => projectionBuilder.BuildFromSkeletalMeshDto(
                context.ObjectPath,
                dto,
                options,
                policy,
                context.TexturePreview,
                context.OwnerModelUri));
        var identity = TryCreateSkeletonIdentity();

        return SaveModel(
            () => new UEModel(
                objectName,
                context.ObjectPath,
                dto,
                options,
                extensionBuilder: BuildExtension(context.ObjectPath, projection, dto.Materials.Length, identity, objectPath, context.OwnerModelUri)));
    }

    public IReadOnlyList<ExportFile> BuildSkeleton(
        string objectName,
        string objectPath,
        ExportOptions options,
        SkeletonDto dto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectPath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dto);

        var identity = TryCreateSkeletonIdentity();
        using var archive = new FArchiveWriter();
        new UEModel(
            objectName,
            objectPath,
            dto,
            options,
            BuildExtension(objectPath, null, 0, identity)).Save(archive);
        return [new ExportFile("uemodel", archive.GetBuffer())];
    }

    private FModelMaterialProjection? TryBuildProjection(
        string objectPath,
        string meshKind,
        int sourceMaterialCount,
        int requiredSectionCount,
        bool lodMaterialTopologyCompatible,
        Func<FModelMaterialProjection> projectionFactory)
    {
        if (!policy.Enabled || policy.MaterialMode == FModelUeFormatMaterialMode.Disabled)
            return null;

        if (!lodMaterialTopologyCompatible)
        {
            if (policy.RequiresCompleteMaterialProjection)
                throw new InvalidOperationException(
                    "Strict material projection requires every serialized LOD to use the same section material topology as LOD0.");
            PublishFallback(
                objectPath,
                "material.lod-topology.incompatible",
                $"V10 FModel material attributes were omitted for {meshKind} because a serialized LOD uses a different section material topology than LOD0.");
            return null;
        }

        var projection = projectionFactory();
        Publish(projection.Diagnostics);
        if (policy.RequiresCompleteMaterialProjection && !projection.IsComplete)
            throw new InvalidOperationException("Strict material projection contains errors; inspect the preceding material/texture diagnostics.");
        if (projection.Materials.Count == 0)
        {
            if (policy.RequiresCompleteMaterialProjection)
                throw new InvalidOperationException("Strict material projection produced no material links.");
            PublishFallback(
                objectPath,
                "material.projection.empty",
                $"V10 FModel material attributes were omitted for {meshKind} because no complete projection was available.");
            return null;
        }
        if (sourceMaterialCount <= 0 || projection.Materials.Count != requiredSectionCount)
        {
            if (policy.RequiresCompleteMaterialProjection)
                throw new InvalidOperationException("Strict material projection does not cover every standard LOD0 material section.");
            PublishFallback(
                objectPath,
                "material.projection.incomplete",
                $"V10 FModel material attributes were omitted for {meshKind} because the projection did not cover every LOD0 section.");
            return null;
        }

        return projection;
    }

    private static bool HasCompatibleLodMaterialTopology(
        IEnumerable<IReadOnlyList<int>> lodMaterialTopologies)
    {
        var topologies = lodMaterialTopologies.ToArray();
        if (topologies.Length <= 1)
            return true;

        var expected = topologies[0];
        return topologies.Skip(1).All(topology => topology.SequenceEqual(expected));
    }

    private Action<FDataAttributeSet>? BuildExtension(
        string objectPath,
        FModelMaterialProjection? projection,
        int sourceMaterialCount,
        FModelSkeletonIdentity? identity,
        string? exporterObjectPath = null,
        string? ownerModelUri = null)
    {
        if (projection is null && identity is null)
            return null;

        return root =>
        {
            try
            {
                root.AddAttributes(staged =>
                {
                    if (projection is not null)
                        attributeWriter.WriteProjection(staged, projection, sourceMaterialCount);
                    if (identity is not null)
                        FModelSkeletonExtensionWriter.WriteModelIdentity(staged, identity);
                });
            }
            catch (Exception error) when (IsLinkedExtensionFailure(error))
            {
                PublishFallback(
                    objectPath,
                    "extension.serialization.failed",
                    $"Linked FModel UEFormat attributes were omitted because extension serialization failed ({error.GetType().Name}); standard UEFormat output was preserved.");
                return;
            }
            // Register only what the attribute writer actually accepted. A Linked
            // fallback must never create requirements for omitted attributes.
            if (deliverySink is not null && projection is { Textures.Count: > 0, TextureResources.Count: > 0 })
            {
                if (exporterObjectPath is null || ownerModelUri is null)
                    throw new InvalidOperationException("Required-resource delivery needs a session-bound owner path.");
                deliverySink(new FModelUeFormatDeliveryDeclaration(
                    exporterObjectPath, ownerModelUri,
                    projection.TextureResources.Select(resource => resource with { Sha256 = resource.Sha256.ToArray() }).ToArray()));
            }
        };
    }

    private bool IsLinkedExtensionFailure(Exception error) =>
        policy.MaterialMode == FModelUeFormatMaterialMode.Linked
        && error is not OutOfMemoryException
        && error is not StackOverflowException
        && error is not AccessViolationException
        && error is not OperationCanceledException;

    private IReadOnlyList<ExportFile> SaveModel(
        Func<UEModel> extendedFactory)
    {
        using var archive = new FArchiveWriter();
        extendedFactory().Save(archive);
        return [new ExportFile("uemodel", archive.GetBuffer())];
    }

    private BoundContext CreateBoundContext(string objectPath)
    {
        if (session is null || sourceObject is null)
            return new BoundContext(objectPath, null, null);

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

    private FModelSkeletonIdentity? TryCreateSkeletonIdentity()
    {
        if (sourceObject is USkeleton skeleton)
            return FModelSkeletonIdentity.FromSkeleton(skeleton);
        if (sourceObject is USkeletalMesh mesh &&
            mesh.Skeleton.TryLoad<USkeleton>(out var resolvedSkeleton) &&
            resolvedSkeleton is not null)
            return FModelSkeletonIdentity.FromSkeleton(resolvedSkeleton);
        return null;
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

    private readonly record struct BoundContext(
        string ObjectPath,
        FModelTextureExportPreview? TexturePreview,
        string? OwnerModelUri);
}
