using System.Collections.Immutable;

namespace CUE4Parse.UeFormat.UEModel;

public sealed record UeModelInspection
{
    public UeModelInspection(
        string objectName,
        byte fileVersion,
        int lodCount,
        bool hasNonzeroGeometry,
        ImmutableArray<int> standardMaterialSlots,
        ImmutableArray<InspectedStandardMaterial> standardMaterials,
        int? materialExtensionVersion,
        ImmutableArray<InspectedMaterialLink> materialLinks,
        int? textureExtensionVersion,
        ImmutableArray<InspectedTextureResource> textureResources,
        ImmutableArray<InspectedPbrTextureBinding> pbrTextureBindings,
        string sha256)
        : this(
            objectName,
            fileVersion,
            lodCount,
            hasNonzeroGeometry,
            standardMaterialSlots,
            standardMaterials,
            materialExtensionVersion,
            materialLinks,
            textureExtensionVersion,
            textureResources,
            pbrTextureBindings,
            ImmutableArray<InspectedLodTopology>.Empty,
            null,
            sha256)
    {
    }

    public UeModelInspection(
        string objectName,
        byte fileVersion,
        int lodCount,
        bool hasNonzeroGeometry,
        ImmutableArray<int> standardMaterialSlots,
        ImmutableArray<InspectedStandardMaterial> standardMaterials,
        int? materialExtensionVersion,
        ImmutableArray<InspectedMaterialLink> materialLinks,
        int? textureExtensionVersion,
        ImmutableArray<InspectedTextureResource> textureResources,
        ImmutableArray<InspectedPbrTextureBinding> pbrTextureBindings,
        ImmutableArray<InspectedLodTopology> lodTopologies,
        InspectedSkeletonTopology? skeletonTopology,
        string sha256)
    {
        ObjectName = objectName;
        FileVersion = fileVersion;
        LodCount = lodCount;
        HasNonzeroGeometry = hasNonzeroGeometry;
        StandardMaterialSlots = standardMaterialSlots.IsDefault ? ImmutableArray<int>.Empty : standardMaterialSlots;
        StandardMaterials = standardMaterials.IsDefault ? ImmutableArray<InspectedStandardMaterial>.Empty : standardMaterials;
        MaterialExtensionVersion = materialExtensionVersion;
        MaterialLinks = materialLinks.IsDefault ? ImmutableArray<InspectedMaterialLink>.Empty : materialLinks;
        TextureExtensionVersion = textureExtensionVersion;
        TextureResources = textureResources.IsDefault ? ImmutableArray<InspectedTextureResource>.Empty : textureResources;
        PbrTextureBindings = pbrTextureBindings.IsDefault ? ImmutableArray<InspectedPbrTextureBinding>.Empty : pbrTextureBindings;
        LodTopologies = lodTopologies.IsDefault ? ImmutableArray<InspectedLodTopology>.Empty : lodTopologies.OrderBy(lod => lod.LodIndex).ToImmutableArray();
        SkeletonTopology = skeletonTopology is null ? null : new InspectedSkeletonTopology(skeletonTopology.Bones, skeletonTopology.BoneCount, skeletonTopology.BoneLayoutHash, skeletonTopology.Identity);
        Sha256 = sha256;
    }

    public string ObjectName { get; }
    public byte FileVersion { get; }
    public int LodCount { get; }
    public bool HasNonzeroGeometry { get; }
    public ImmutableArray<int> StandardMaterialSlots { get; }
    public ImmutableArray<InspectedStandardMaterial> StandardMaterials { get; }
    public int? MaterialExtensionVersion { get; }
    public ImmutableArray<InspectedMaterialLink> MaterialLinks { get; }
    public int? TextureExtensionVersion { get; }
    public ImmutableArray<InspectedTextureResource> TextureResources { get; }
    public ImmutableArray<InspectedPbrTextureBinding> PbrTextureBindings { get; }
    public ImmutableArray<InspectedLodTopology> LodTopologies { get; }
    public InspectedSkeletonTopology? SkeletonTopology { get; }
    public string Sha256 { get; }
}

public sealed record InspectedMaterialLink(
    int SlotIndex,
    int SourceMaterialIndex,
    string MaterialName,
    string UnrealMaterialPath,
    string MaterialJsonUri,
    int ParameterCount);

public sealed record InspectedStandardMaterial(
    int SlotIndex,
    string UnrealMaterialPath);

public sealed record InspectedTextureChunk(
    int Version,
    ImmutableArray<InspectedTextureResource> Textures,
    ImmutableArray<InspectedPbrTextureBinding> Bindings);

public sealed record InspectedTextureResource(
    string StableId,
    string LogicalResourceUri,
    bool IsEmbedded,
    int ByteLength,
    ImmutableArray<byte> Sha256,
    FModelTextureResourceLocation LocationMode = FModelTextureResourceLocation.ExportRoot);

public sealed record InspectedPbrTextureBinding(
    string MaterialId,
    PbrMapKind MapKind,
    string TextureResourceId,
    bool IsSrgb);
