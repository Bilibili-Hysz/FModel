using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.UEScene;
using CUE4Parse_Conversion.UEScene.World;
using CUE4Parse_Conversion.Formats.World;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.World;

/// <summary>Provides byte/hash/LOD metadata for one world dependency.</summary>
public interface IUeSceneWorldAssetProvider
{
    bool TryDescribe(UObject resource, WorldExportAssetKind kind, string logicalUri, out UeSceneWorldAssetPlan asset);

    /// <summary>
    /// Returns sidecar assets discovered while producing the primary asset. The
    /// default keeps the P2 provider contract source-compatible for providers that
    /// only know how to describe a mesh.
    /// </summary>
    IReadOnlyList<UeSceneWorldAssetPlan> GetDependencies(UeSceneWorldAssetPlan asset) => Array.Empty<UeSceneWorldAssetPlan>();
}

/// <summary>
/// Converts upstream WorldDto/WorldAssetPaths into the host-neutral P2 plan.
/// It intentionally omits components whose producer metadata is unavailable;
/// the omission is made visible as a scene diagnostic instead of invalid wire.
/// </summary>
public sealed class UeSceneWorldPlanner
{
    private const ushort MissingMeshDiagnosticCode = 3;
    private const ushort UnsupportedSkeletalMeshDiagnosticCode = 10;
    private readonly IUeSceneWorldAssetProvider assetProvider;

    public UeSceneWorldPlanner(IUeSceneWorldAssetProvider assetProvider)
    {
        this.assetProvider = assetProvider ?? throw new ArgumentNullException(nameof(assetProvider));
    }

    public UeSceneWorldPlan Plan(WorldDto world, WorldAssetPaths paths)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(paths);

        var assets = new Dictionary<ulong, UeSceneWorldAssetPlan>();
        var actors = new List<UeSceneWorldActorPlan>();
        foreach (var actor in world.Actors)
            AppendActor(actor, 0, paths, assets, actors);

        return new UeSceneWorldPlan(
            UeSceneWorldPathIdentity.BuildCanonicalPackageObjectPath(world.Path),
            UeSceneWorldPathIdentity.BuildCanonicalPackageObjectPath(world.Path) + ":PersistentLevel",
            actors,
            assets.Values.ToArray(),
            SourceGame: null,
            SourceEngine: null);
    }

    private void AppendActor(
        ActorDto actor,
        ulong parentActorId,
        WorldAssetPaths paths,
        Dictionary<ulong, UeSceneWorldAssetPlan> assets,
        List<UeSceneWorldActorPlan> output)
    {
        var actorSourcePath = BuildActorSourcePath(actor);
        var actorId = StableId("actor", actorSourcePath);
        var components = new List<UeSceneWorldComponentPlan>();
        var actorDiagnostics = new List<UeSceneWorldDiagnosticPlan>();
        if (actor.RootComponent is { } root)
            AppendComponent(root, actorId, 0, paths, assets, components, actorDiagnostics, output);

        output.Add(new UeSceneWorldActorPlan(
            actorId,
            parentActorId,
            actor.Name,
            actor.GetType().Name,
            actorSourcePath,
            ToTransform(actor.RootComponent?.Transform ?? CUE4Parse.UE4.Objects.Core.Math.FTransform.Identity),
            components,
            actorDiagnostics));

        if (actor.RootComponent is { AttachedActors.Count: > 0 } rootWithActors)
        {
            foreach (var attachedActor in rootWithActors.AttachedActors)
                AppendActor(attachedActor, actorId, paths, assets, output);
        }
    }

    private void AppendComponent(
        SceneComponentDto component,
        ulong actorId,
        ulong parentComponentId,
        WorldAssetPaths paths,
        Dictionary<ulong, UeSceneWorldAssetPlan> assets,
        List<UeSceneWorldComponentPlan> output,
        List<UeSceneWorldDiagnosticPlan> actorDiagnostics,
        List<UeSceneWorldActorPlan> actorsOutput)
    {
        var componentSourcePath = BuildComponentSourcePath(component);
        var componentId = StableId("component", componentSourcePath);
        var nextParent = parentComponentId;
        if (component is SkinnedMeshComponentDto)
        {
            actorDiagnostics.Add(new UeSceneWorldDiagnosticPlan(
                1,
                2,
                UnsupportedSkeletalMeshDiagnosticCode,
                actorId,
                componentSourcePath,
                "Skeletal mesh components are not supported by UEScene V1; component was omitted without static-mesh substitution."));
        }
        else if (component is MeshComponentDto mesh)
        {
            if (!paths.TryGet(mesh.MeshPtr, out var logicalUri)
                || !mesh.MeshPtr.TryLoad<UObject>(out var meshObject)
                || !assetProvider.TryDescribe(meshObject, WorldExportAssetKind.Mesh, logicalUri, out var asset))
            {
                actorDiagnostics.Add(new UeSceneWorldDiagnosticPlan(
                    1,
                    2,
                    MissingMeshDiagnosticCode,
                    actorId,
                    componentSourcePath,
                    "Static mesh dependency metadata was unavailable; component was omitted from UEScene."));
            }
            else
            {
                AddAsset(assets, asset);
                foreach (var dependency in assetProvider.GetDependencies(asset))
                    AddAsset(assets, dependency);

                output.Add(new UeSceneWorldComponentPlan(
                    componentId,
                    parentComponentId,
                    component is InstancedStaticMeshComponentDto
                        ? ComponentKind.InstancedStaticMesh
                        : ComponentKind.StaticMesh,
                    component.Name,
                    component.GetType().Name,
                    componentSourcePath,
                    asset.AssetId,
                    ToTransform(component.Transform),
                    component is not PrimitiveComponentDto primitive || primitive.IsVisible,
                    component is InstancedStaticMeshComponentDto instanced
                        ? instanced.Transforms.Select(ToInstanceTransform).ToArray()
                        : Array.Empty<UeSceneWorldInstanceTransform>(),
                    Array.Empty<UeSceneWorldMaterialOverridePlan>(),
                    Array.Empty<UeSceneWorldDiagnosticPlan>()));
                nextParent = componentId;
            }
        }
        else if (component is LightComponentBaseDto)
        {
            actorDiagnostics.Add(new UeSceneWorldDiagnosticPlan(
                1,
                2,
                9,
                actorId,
                componentSourcePath,
                "Light component planning is reserved for the next P2 increment."));
        }

        foreach (var child in component.Children)
            AppendComponent(child, actorId, nextParent, paths, assets, output, actorDiagnostics, actorsOutput);
        foreach (var attachedActor in component.AttachedActors)
            AppendActor(attachedActor, actorId, paths, assets, actorsOutput);
    }

    private static void AddAsset(Dictionary<ulong, UeSceneWorldAssetPlan> assets, UeSceneWorldAssetPlan asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.AssetId == 0)
            throw new InvalidDataException("World asset IDs must be nonzero.");

        if (assets.TryGetValue(asset.AssetId, out var existing))
        {
            if (!string.Equals(existing.LogicalUri, asset.LogicalUri, StringComparison.Ordinal)
                || existing.ByteLength != asset.ByteLength
                || !existing.Sha256.AsSpan().SequenceEqual(asset.Sha256))
            {
                throw new InvalidDataException($"World asset ID {asset.AssetId} was claimed by conflicting resource snapshots.");
            }
            return;
        }

        assets.Add(asset.AssetId, asset);
    }

    private static string BuildActorSourcePath(ActorDto actor) =>
        UeSceneWorldPathIdentity.BuildCanonicalObjectPath(actor, actor.Name);

    private static string BuildComponentSourcePath(SceneComponentDto component) =>
        BuildActorSourcePath(component.Owner) + ":" + component.Name;

    private static ulong StableId(string domain, string identity)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(domain + "\0" + identity));
        var value = BitConverter.ToUInt64(bytes, 0);
        return value == 0 ? 1UL : value;
    }

    private static UeSceneWorldTransform ToTransform(CUE4Parse.UE4.Objects.Core.Math.FTransform transform) => new(
        transform.Translation.X,
        transform.Translation.Y,
        transform.Translation.Z,
        transform.Rotation.X,
        transform.Rotation.Y,
        transform.Rotation.Z,
        transform.Rotation.W,
        transform.Scale3D.X,
        transform.Scale3D.Y,
        transform.Scale3D.Z);

    private static UeSceneWorldInstanceTransform ToInstanceTransform(CUE4Parse.UE4.Objects.Core.Math.FTransform transform) => new(
        transform.Translation.X,
        transform.Translation.Y,
        transform.Translation.Z,
        transform.Rotation.X,
        transform.Rotation.Y,
        transform.Rotation.Z,
        transform.Rotation.W,
        transform.Scale3D.X,
        transform.Scale3D.Y,
        transform.Scale3D.Z);
}
