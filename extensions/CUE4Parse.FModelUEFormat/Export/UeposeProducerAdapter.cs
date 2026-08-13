namespace CUE4Parse.UeFormat.Export;

/// <summary>Vendor-neutral UEPOSE producer with target skeleton extension support.</summary>
public sealed class UeposeProducerAdapter : ProducerAdapter
{
    public UeposeProducerAdapter() : base(ProducerKind.Uepose) { }
    protected override string SkeletonExtensionId => "FMODEL_TARGET_SKELETON";
}
