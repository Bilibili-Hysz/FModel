using System.Collections.Immutable;
using System.Text;
using CUE4Parse.UeFormat.Protocol;
using CUE4Parse.UeFormat.UEModel;

namespace CUE4Parse.UeFormat.Export;

/// <summary>Vendor-neutral UEMODEL producer with material and model skeleton extensions.</summary>
public sealed class UemodelProducerAdapter : ProducerAdapter
{
    public UemodelProducerAdapter() : base(ProducerKind.Uemodel) { }
    protected override string SkeletonExtensionId => "FMODEL_SKELETON_IDENTITY";

    protected override IEnumerable<(string Subject, Func<UeFormatExtensionChunk?> Write)> GetExtensionWriters(ProducerSnapshot snapshot)
    {
        var skeletonTopologyValid = false;
        yield return ("LODS", () => ValidateLodTopology(snapshot));
        if (snapshot.TopologySnapshot?.Skeleton is not null)
            yield return ("SKELETON", () =>
            {
                ValidateSkeletonTopology(snapshot);
                skeletonTopologyValid = true;
                return null;
            });
        if (snapshot.MaterialLinks.Count > 0)
            yield return ("FMODEL_MATERIALS", () => WriteMaterialLinks(snapshot));
        if (snapshot.TextureResourceSet is not null)
            yield return ("FMODEL_TEXTURES", () => FModelTextureChunkWriter.WriteV1(snapshot.TextureResourceSet));
        if (snapshot.SkeletonIdentity is not null)
            yield return ("FMODEL_SKELETON_IDENTITY", () => snapshot.TopologySnapshot?.Skeleton is null
                ? WriteSkeletonIdentity(snapshot)
                : skeletonTopologyValid ? WriteSkeletonIdentity(snapshot) : null);
    }

    private static UeFormatExtensionChunk? ValidateLodTopology(ProducerSnapshot snapshot)
    {
        var topology = snapshot.TopologySnapshot ?? throw new NotSupportedException();
        UeModelTopologyInspection.ValidateLodTopologies(topology.Lods);
        ValidateMaterialCrossLinks(snapshot, topology);
        if (topology.Lods.Count > 1)
        {
            var expectedSlots = topology.Lods[0].MaterialSlots
                .OrderBy(slot => slot.SlotIndex)
                .Select(slot => (slot.SlotIndex, slot.UnrealMaterialPath))
                .ToArray();
            if (topology.Lods.Skip(1).Any(lod => !lod.MaterialSlots
                    .OrderBy(slot => slot.SlotIndex)
                    .Select(slot => (slot.SlotIndex, slot.UnrealMaterialPath))
                    .SequenceEqual(expectedSlots)))
                throw new ProducerTopologyValidationException("producer.lod-material-topology-divergence");
        }

        return null;
    }

    private static void ValidateMaterialCrossLinks(ProducerSnapshot snapshot, ProducerTopologySnapshot topology)
    {
        if (snapshot.MaterialLinks.Count == 0)
            return;

        var materialTopologies = snapshot.LodMaterialTopologies
            .OrderBy(lod => lod.LodIndex)
            .ToArray();
        if (materialTopologies.Length != topology.Lods.Count)
            throw new ProducerTopologyValidationException("producer.incomplete-lod-material-cross-link");

        for (var index = 0; index < topology.Lods.Count; index++)
        {
            var standardLod = topology.Lods[index];
            var materialLod = materialTopologies[index];
            if (standardLod.LodIndex != materialLod.LodIndex || standardLod.MaterialSlots.Count != materialLod.MaterialIds.Count)
                throw new ProducerTopologyValidationException("producer.incomplete-lod-material-cross-link");

            // The current producer contract exposes material IDs and logical URIs, but no
            // identity binding from an inspected Unreal path to either value. Do not infer one.
            if (standardLod.MaterialSlots.Count > 0)
                throw new ProducerTopologyValidationException("producer.incomplete-lod-material-cross-link");
        }
    }

    private static UeFormatExtensionChunk? ValidateSkeletonTopology(ProducerSnapshot snapshot)
    {
        var skeletonTopology = snapshot.TopologySnapshot?.Skeleton ?? throw new NotSupportedException();
        ValidateSkeletonTopology(snapshot, skeletonTopology);
        return null;
    }

    private static void ValidateSkeletonTopology(ProducerSnapshot snapshot, InspectedSkeletonTopology skeletonTopology)
    {
        var skeleton = UeModelTopologyInspection.ValidateSkeletonTopology(skeletonTopology);
        if (snapshot.SkeletonIdentity is null)
            return;
        if (skeleton.Identity is null)
            throw new ProducerTopologyValidationException("producer.incomplete-skeleton-identity");
        if (!IdentityTupleEquals(snapshot.SkeletonIdentity, skeleton.Identity))
            throw new InvalidDataException("Producer skeleton identity must match standard skeleton topology.");
    }

    private static bool IdentityTupleEquals(SkeletonIdentityV1 producer, SkeletonIdentityV1 inspected) =>
        string.Equals(producer.SkeletonPath, inspected.SkeletonPath, StringComparison.Ordinal) &&
        producer.SkeletonGuid == inspected.SkeletonGuid &&
        producer.BoneLayoutHash.AsSpan().SequenceEqual(inspected.BoneLayoutHash.AsSpan());

    private static UeFormatExtensionChunk WriteSkeletonIdentity(ProducerSnapshot snapshot)
    {
        var skeletonTopology = snapshot.TopologySnapshot?.Skeleton ?? throw new NotSupportedException();
        ValidateSkeletonTopology(snapshot, skeletonTopology);
        return SkeletonIdentityChunkWriter.WriteModelIdentity(snapshot.SkeletonIdentity!);
    }

    private static UeFormatExtensionChunk WriteMaterialLinks(ProducerSnapshot snapshot)
    {
        var links = snapshot.MaterialLinks
            .Select((link, index) => new StaticMeshMaterialLink(
                index,
                index,
                link.MaterialId,
                link.MaterialId,
                link.MaterialUri))
            .ToImmutableArray();

        var bytes = FModelMaterialLinkChunkWriter.WriteV2(new StaticMeshMaterialLinkSet(snapshot.MaterialLinks.Count, links));
        return ParseMaterialChunk(bytes);
    }

    private static UeFormatExtensionChunk ParseMaterialChunk(byte[] bytes)
    {
        var reader = new BinaryReader(new MemoryStream(bytes, writable: false), Encoding.UTF8, leaveOpen: false);
        var id = ReadString(reader);
        var count = reader.ReadInt32();
        var length = reader.ReadInt32();
        var payload = reader.ReadBytes(length);
        if (payload.Length != length || reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("FMODEL_MATERIALS chunk envelope was malformed.");
        return new UeFormatExtensionChunk(id, count, payload);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new InvalidDataException("Chunk string exceeded remaining input.");
        return Encoding.UTF8.GetString(bytes);
    }
}
