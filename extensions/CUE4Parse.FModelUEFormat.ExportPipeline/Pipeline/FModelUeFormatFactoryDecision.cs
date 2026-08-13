namespace CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;

/// <summary>Factory disposition at the queue-time host boundary.</summary>
public enum FModelUeFormatFactoryDisposition
{
    NotHandled = 0,
    Owned = 1
}

/// <summary>Host-neutral decision produced before a typed upstream exporter is queued.</summary>
public sealed record FModelUeFormatFactoryDecision(
    FModelUeFormatFactoryDisposition Disposition,
    string ReasonCode,
    string? ExportType,
    FModelUeFormatPolicy Policy)
{
    public bool IsOwned => Disposition == FModelUeFormatFactoryDisposition.Owned;

    public static FModelUeFormatFactoryDecision Disabled(string? exportType = null) =>
        new(FModelUeFormatFactoryDisposition.NotHandled, "v3.activation.disabled", exportType, FModelUeFormatPolicy.Disabled);

    public static FModelUeFormatFactoryDecision NotHandled(string reasonCode, string? exportType, FModelUeFormatPolicy policy) =>
        new(FModelUeFormatFactoryDisposition.NotHandled, reasonCode, exportType, policy);

    public static FModelUeFormatFactoryDecision Owned(string exportType, FModelUeFormatPolicy policy) =>
        new(FModelUeFormatFactoryDisposition.Owned, "v3.exporter.owned", exportType, policy);
}
