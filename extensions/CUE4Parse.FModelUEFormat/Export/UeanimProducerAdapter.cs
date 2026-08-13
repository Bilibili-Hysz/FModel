namespace CUE4Parse.UeFormat.Export;

/// <summary>Vendor-neutral UEANIM producer with target skeleton extension support.</summary>
public sealed class UeanimProducerAdapter : ProducerAdapter
{
    public UeanimProducerAdapter() : base(ProducerKind.Ueanim) { }
    protected override string SkeletonExtensionId => "FMODEL_TARGET_SKELETON";
}
