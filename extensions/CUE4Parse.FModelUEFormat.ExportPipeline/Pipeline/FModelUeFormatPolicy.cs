namespace CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;

/// <summary>
/// Selects the root against which V3 material and texture logical URIs are
/// resolved. Standalone UEMODEL exports retain the upstream export-root shape;
/// the retained GameExportTree value is compatibility-only and does not imply
/// an active scene producer.
/// </summary>
public enum FModelUeFormatResourceUriRoot : byte
{
    /// <summary>Logical paths reproduce the upstream export root.</summary>
    ExportRoot = 0,
    /// <summary>Compatibility logical paths rooted at a project Content/ tree.</summary>
    GameExportTree = 1,
    /// <summary>Logical paths are relative to the owning .uemodel file.</summary>
    RelativeToOwner = 2
}

/// <summary>Immutable per-session policy for the new FModel export pipeline.</summary>
public sealed record FModelUeFormatPolicy
{
    public static FModelUeFormatPolicy Disabled { get; } = new(
        Enabled: false,
        MaterialMode: FModelUeFormatMaterialMode.Disabled,
        PreserveStandardMaterials: true,
        EmitTextureLinks: false,
        EvidenceGeneration: "fmodel-export-pipeline-v2/v3-adapter-v1");

    public FModelUeFormatPolicy(
        bool Enabled,
        FModelUeFormatMaterialMode MaterialMode,
        bool PreserveStandardMaterials = true,
        bool EmitTextureLinks = true,
        string EvidenceGeneration = "fmodel-export-pipeline-v2/v3-adapter-v1",
        FModelUeFormatResourceUriRoot ResourceUriRoot = FModelUeFormatResourceUriRoot.RelativeToOwner)
    {
        if (string.IsNullOrWhiteSpace(EvidenceGeneration))
            throw new ArgumentException("Evidence generation is required.", nameof(EvidenceGeneration));
        if (!Enum.IsDefined(ResourceUriRoot))
            throw new ArgumentOutOfRangeException(nameof(ResourceUriRoot));
        if (!PreserveStandardMaterials)
            throw new ArgumentException("V3 must not disable the standard MATERIALS chunk.", nameof(PreserveStandardMaterials));
        if (!Enabled && MaterialMode != FModelUeFormatMaterialMode.Disabled)
            throw new ArgumentException("A disabled policy cannot request material links.", nameof(MaterialMode));

        this.Enabled = Enabled;
        this.MaterialMode = MaterialMode;
        this.PreserveStandardMaterials = PreserveStandardMaterials;
        this.EmitTextureLinks = EmitTextureLinks && MaterialMode != FModelUeFormatMaterialMode.Disabled;
        this.EvidenceGeneration = EvidenceGeneration;
        this.ResourceUriRoot = ResourceUriRoot;
    }

    public bool Enabled { get; }
    public FModelUeFormatMaterialMode MaterialMode { get; }
    public bool PreserveStandardMaterials { get; }
    public bool EmitTextureLinks { get; }
    public string EvidenceGeneration { get; }
    public FModelUeFormatResourceUriRoot ResourceUriRoot { get; }

    /// <summary>
    /// Returns whether two queue-owned exporters were created from the same immutable policy snapshot.
    /// A URI-root match alone is insufficient because material mode and texture-link policy also affect bytes.
    /// </summary>
    public bool IsCompatibleWith(FModelUeFormatPolicy? other) =>
        other is not null && Equals(other);

    public bool OwnsStaticMesh => Enabled;
    public bool OwnsSkeletalMesh => Enabled;
    public bool RequiresCompleteMaterialProjection => MaterialMode == FModelUeFormatMaterialMode.Strict;

    /// <summary>Returns an otherwise identical policy with another resource URI root.</summary>
    public FModelUeFormatPolicy WithResourceUriRoot(FModelUeFormatResourceUriRoot resourceUriRoot) =>
        ResourceUriRoot == resourceUriRoot
            ? this
            : new FModelUeFormatPolicy(
                Enabled,
                MaterialMode,
                PreserveStandardMaterials,
                EmitTextureLinks,
                EvidenceGeneration,
                resourceUriRoot);
}
