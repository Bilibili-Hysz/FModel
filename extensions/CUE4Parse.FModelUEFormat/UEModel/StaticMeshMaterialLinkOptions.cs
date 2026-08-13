namespace CUE4Parse.UeFormat.UEModel;

/// <summary>
/// Explicit opt-in marker for package-backed static-mesh material link serialization.
/// The codec intentionally contains no CUE4Parse, FModel, or UI dependency.
/// </summary>
public sealed record StaticMeshMaterialLinkOptions
{
    public static StaticMeshMaterialLinkOptions EmbeddedV2 { get; } = new();
}
