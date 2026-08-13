using System.Collections.Immutable;
using CUE4Parse.UeFormat.Protocol;

namespace CUE4Parse.UeFormat.UEModel;

public sealed record InspectedLodMaterialSlot
{
    public InspectedLodMaterialSlot(int slotIndex, string unrealMaterialPath)
    {
        SlotIndex = slotIndex;
        UnrealMaterialPath = unrealMaterialPath ?? throw new ArgumentNullException(nameof(unrealMaterialPath));
    }

    public int SlotIndex { get; }
    public string UnrealMaterialPath { get; }
}

public sealed record InspectedLodTopology
{
    private readonly int[] _indices;
    private readonly InspectedLodMaterialSlot[] _materialSlots;

    public InspectedLodTopology(int lodIndex, int vertexCount, int indexCount, IEnumerable<int> indices, IEnumerable<InspectedLodMaterialSlot> materialSlots)
    {
        LodIndex = lodIndex;
        VertexCount = vertexCount;
        IndexCount = indexCount;
        _indices = (indices ?? throw new ArgumentNullException(nameof(indices))).ToArray();
        _materialSlots = (materialSlots ?? throw new ArgumentNullException(nameof(materialSlots)))
            .Select(slot => slot ?? throw new ArgumentException("LOD material slots cannot contain null entries.", nameof(materialSlots)))
            .ToArray();
    }

    public int LodIndex { get; }
    public int VertexCount { get; }
    public int IndexCount { get; }
    public IReadOnlyList<int> Indices => Array.AsReadOnly(_indices.ToArray());
    public IReadOnlyList<InspectedLodMaterialSlot> MaterialSlots => Array.AsReadOnly(_materialSlots.ToArray());
}

public sealed record InspectedSkeletonBone
{
    public InspectedSkeletonBone(int boneIndex, string name, int parentIndex)
    {
        BoneIndex = boneIndex;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ParentIndex = parentIndex;
    }

    public int BoneIndex { get; }
    public string Name { get; }
    public int ParentIndex { get; }
}

public sealed record InspectedSkeletonTopology
{
    private readonly InspectedSkeletonBone[] _bones;
    private readonly byte[] _boneLayoutHash;

    public InspectedSkeletonTopology(IEnumerable<InspectedSkeletonBone> bones, int boneCount, ImmutableArray<byte> boneLayoutHash, SkeletonIdentityV1? identity)
    {
        _bones = (bones ?? throw new ArgumentNullException(nameof(bones)))
            .Select(bone => bone ?? throw new ArgumentException("Skeleton bones cannot contain null entries.", nameof(bones)))
            .ToArray();
        BoneCount = boneCount;
        if (boneLayoutHash.IsDefault) throw new ArgumentException("BoneLayoutHash must be initialized.", nameof(boneLayoutHash));
        _boneLayoutHash = boneLayoutHash.ToArray();
        Identity = identity is null ? null : new SkeletonIdentityV1(identity.SkeletonPath, identity.SkeletonGuid, identity.BoneLayoutHash);
    }

    public IReadOnlyList<InspectedSkeletonBone> Bones => Array.AsReadOnly(_bones.ToArray());
    public int BoneCount { get; }
    public ImmutableArray<byte> BoneLayoutHash => ImmutableArray.Create(_boneLayoutHash);
    public SkeletonIdentityV1? Identity { get; }
}

public static class UeModelTopologyInspection
{
    public static UeModelInspection Validate(UeModelInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        var lodTopologies = ValidateLodTopologies(inspection.LodTopologies);
        var skeletonTopology = inspection.SkeletonTopology is null ? null : ValidateSkeletonTopology(inspection.SkeletonTopology);

        return new UeModelInspection(
            inspection.ObjectName,
            inspection.FileVersion,
            inspection.LodCount,
            inspection.HasNonzeroGeometry,
            inspection.StandardMaterialSlots,
            inspection.StandardMaterials,
            inspection.MaterialExtensionVersion,
            inspection.MaterialLinks,
            inspection.TextureExtensionVersion,
            inspection.TextureResources,
            inspection.PbrTextureBindings,
            lodTopologies,
            skeletonTopology,
            inspection.Sha256);
    }

    public static ImmutableArray<InspectedLodTopology> ValidateLodTopologies(IEnumerable<InspectedLodTopology> topologies)
    {
        var materialized = (topologies ?? throw new ArgumentNullException(nameof(topologies)))
            .Select(topology => topology ?? throw new ArgumentException("LOD topologies cannot contain null entries.", nameof(topologies)))
            .OrderBy(topology => topology.LodIndex)
            .ToArray();

        var seenLods = new HashSet<int>();
        foreach (var topology in materialized)
        {
            if (topology.LodIndex < 0) throw new InvalidDataException("LOD indices must be non-negative.");
            if (!seenLods.Add(topology.LodIndex)) throw new InvalidDataException("LOD indices must be unique.");
            if (topology.VertexCount < 0) throw new InvalidDataException("VertexCount must be non-negative.");
            if (topology.IndexCount < 0) throw new InvalidDataException("IndexCount must be non-negative.");
            if (topology.IndexCount != topology.Indices.Count) throw new InvalidDataException("IndexCount must match indices length.");
            if (topology.Indices.Any(index => index < 0 || index >= topology.VertexCount))
                throw new InvalidDataException("Indices must be within [0, VertexCount).");

            var seenSlots = new HashSet<int>();
            foreach (var slot in topology.MaterialSlots)
            {
                if (slot.SlotIndex < 0) throw new InvalidDataException("Material slot indices must be non-negative.");
                if (!seenSlots.Add(slot.SlotIndex)) throw new InvalidDataException("Material slot indices must be unique within a LOD.");
                if (string.IsNullOrWhiteSpace(slot.UnrealMaterialPath)) throw new InvalidDataException("Material slot Unreal paths must be non-empty.");
                ValidateLogicalUnrealMaterialPath(slot.UnrealMaterialPath);
            }
        }

        return materialized
            .Select(topology => new InspectedLodTopology(topology.LodIndex, topology.VertexCount, topology.IndexCount, topology.Indices, topology.MaterialSlots))
            .ToImmutableArray();
    }

    public static InspectedSkeletonTopology ValidateSkeletonTopology(InspectedSkeletonTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ValidateSkeletonTopologyStructure(topology);

        var expectedHash = SkeletonIdentityV1.ComputeLayoutHash(topology.Bones.Select(bone => new SkeletonBone(bone.Name, bone.ParentIndex)));
        if (!topology.BoneLayoutHash.AsSpan().SequenceEqual(expectedHash.AsSpan()))
            throw new InvalidDataException("Skeleton bone layout hash must match Skeleton Identity V1 layout hash.");
        if (topology.Identity is not null && !topology.Identity.BoneLayoutHash.AsSpan().SequenceEqual(expectedHash.AsSpan()))
            throw new InvalidDataException("Skeleton bone layout hash must match Skeleton Identity V1 layout hash.");

        return new InspectedSkeletonTopology(topology.Bones, topology.BoneCount, topology.BoneLayoutHash, topology.Identity);
    }

    internal static void ValidateSkeletonTopologyStructure(InspectedSkeletonTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        if (topology.BoneCount != topology.Bones.Count) throw new InvalidDataException("Skeleton bone count must match bones length.");

        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var seenIndices = new HashSet<int>();
        for (var position = 0; position < topology.Bones.Count; position++)
        {
            var bone = topology.Bones[position];
            if (bone.BoneIndex < 0 || bone.BoneIndex >= topology.BoneCount || !seenIndices.Add(bone.BoneIndex))
                throw new InvalidDataException("Skeleton bone indices must match positions exactly once as 0..BoneCount-1.");
            if (bone.BoneIndex != position)
                throw new InvalidDataException("Skeleton bone indices must match positions exactly once as 0..BoneCount-1.");
            if (!seenNames.Add(bone.Name)) throw new InvalidDataException("Skeleton bone names must be unique.");
            if (bone.ParentIndex < -1 || bone.ParentIndex >= bone.BoneIndex || (bone.ParentIndex >= 0 && !seenIndices.Contains(bone.ParentIndex)))
                throw new InvalidDataException("Skeleton parents must be -1 or reference an earlier bone.");
        }
    }

    private static void ValidateLogicalUnrealMaterialPath(string path)
    {
        try
        {
            LogicalUnrealPath.Validate(path, nameof(path));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Material slot Unreal paths must be valid logical Unreal paths.", exception);
        }
    }
}
