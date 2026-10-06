namespace CUE4Parse.FModelUEFormat.ExportPipeline.Materials;

/// <summary>
/// Conservative name declarations compatible with the frozen ORM consumer.
/// This does not inspect pixels or prove a game's shader interpretation.
/// Unknown packed maps require a verified game-specific conversion first.
/// </summary>
public static class FModelPackedMaskLayout
{
    public static bool IsDeclaredOrm(string? parameter) =>
        new string((parameter ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant() is
            "orm" or "baseorm" or "occlusionroughnessmetallictexture" or "rgbaormetallictexture";
}
