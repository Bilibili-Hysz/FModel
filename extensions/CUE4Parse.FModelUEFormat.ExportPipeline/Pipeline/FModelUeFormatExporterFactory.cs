namespace CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;

/// <summary>Typed, host-neutral context used at the new FModel queue-time seam.</summary>
public sealed record FModelUeFormatFactoryContext(
    string ExportType,
    string RequestedFormat,
    bool IsBulkExport,
    FModelUeFormatPolicy Policy);

/// <summary>Queue-time factory boundary. NP1 returns a disposition; NP2 adds typed exporter creation.</summary>
public interface IFModelUeFormatExporterFactory
{
    FModelUeFormatFactoryDecision Decide(FModelUeFormatFactoryContext context);
}

/// <summary>Default-off policy decision for enabled top-level Static and Skeletal Mesh UEFormat requests.</summary>
public sealed class FModelUeFormatExporterFactory(FModelUeFormatPolicy policy) : IFModelUeFormatExporterFactory
{
    public FModelUeFormatPolicy Policy { get; } = policy ?? throw new ArgumentNullException(nameof(policy));

    public FModelUeFormatFactoryDecision Decide(FModelUeFormatFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!Policy.Enabled || !context.Policy.Enabled)
            return FModelUeFormatFactoryDecision.Disabled(context.ExportType);

        var isOwnedMeshType = string.Equals(context.ExportType, "UStaticMesh", StringComparison.Ordinal)
                              || string.Equals(context.ExportType, "USkeletalMesh", StringComparison.Ordinal);
        if (!isOwnedMeshType)
            return FModelUeFormatFactoryDecision.NotHandled("v3.exporter.unsupported-type", context.ExportType, Policy);
        if (!string.Equals(context.RequestedFormat, "UEFormat", StringComparison.OrdinalIgnoreCase))
            return FModelUeFormatFactoryDecision.NotHandled("v3.exporter.format-not-ueformat", context.ExportType, Policy);
        return FModelUeFormatFactoryDecision.Owned(context.ExportType, Policy);
    }
}