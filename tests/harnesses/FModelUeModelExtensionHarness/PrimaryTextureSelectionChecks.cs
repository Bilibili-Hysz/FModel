using System.Reflection;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.FModelUEFormat.ExportPipeline.Diagnostics;
using CUE4Parse.FModelUEFormat.ExportPipeline.Materials;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.UeFormat.UEModel;

// Anonymous regressions for the R63 real-output findings. No private assets,
// material JSON fallback, Blender runtime or changes to upstream parsers.
internal static class PrimaryTextureSelectionChecks
{
    internal static void Run()
    {
        var isCubeMap = typeof(FModelMaterialProjectionBuilder).GetMethod("IsCubeMap", BindingFlags.Static | BindingFlags.NonPublic)!;
        if (!(bool)isCubeMap.Invoke(null, [new UTextureCube()])!
            || !(bool)isCubeMap.Invoke(null, [new UTextureCubeArray()])!
            || (bool)isCubeMap.Invoke(null, [new UTexture2D()])!)
            throw new InvalidOperationException("Runtime cube classification must not trust the inherited false flag.");
        var primary = Input("BaseColor", "Body", PbrMapKind.BaseColor);
        var normal = Input("NormalMap", "BodyNormal", PbrMapKind.Normal);
        var overlay = Input("Overlay_BaseColor", "Overlay", PbrMapKind.BaseColor);
        var overlayNormal = Input("Overlay_Normalmap", "OverlayNormal", PbrMapKind.Normal);
        AssertSelection("explicit surface before overwritten aliases",
            [primary, normal, overlay, overlayNormal,
             Input("PM_Diffuse", "Overlay", PbrMapKind.BaseColor),
             Input("PM_Normals", "OverlayNormal", PbrMapKind.Normal)],
            "BaseColor", "NormalMap");
        AssertSelection("wrinkle and detail normals are auxiliary",
            [normal, Input("FirstDetailNormal", "Detail", PbrMapKind.Normal),
             Input("WrinkleNormalMap", "Expression", PbrMapKind.Normal),
             Input("PM_Normals", "Expression", PbrMapKind.Normal)], "NormalMap");
        AssertSelection("cubemap cannot become surface BaseColor",
            [Input("EyeWhiteBaseColor", "Eye", PbrMapKind.BaseColor),
             Input("IrisBaseColor", "Iris", PbrMapKind.BaseColor),
             Input("PM_Diffuse", "Reflection", PbrMapKind.BaseColor) with { SourceTextureIsCubeMap = true },
             Input("CubemapReflectionTexture", "Reflection", null) with { SourceTextureIsCubeMap = true }],
            "EyeWhiteBaseColor");
        AssertSelection("official primary names before generic heuristics",
            [Input("PM_Diffuse", "Wrong", PbrMapKind.BaseColor),
             Input("Skin_BaseColor", "Skin", PbrMapKind.BaseColor),
             Input("DiffuseTexture", "Body", PbrMapKind.BaseColor)], "DiffuseTexture");
        AssertSelection("explicit ORM before overwritten mask alias",
            [Input("ORM", "Packed", PbrMapKind.Orm),
             Input("PM_SpecularMasks", "Placeholder", PbrMapKind.Orm)], "ORM");
        AssertSelection("auxiliary aliases cannot resurrect a rejected map",
            [overlay, Input("PM_Diffuse", "Overlay", PbrMapKind.BaseColor),
             Input("Overlay", "Overlay", PbrMapKind.BaseColor)]);
        AssertSelection("shared image does not invalidate a real primary parameter",
            [Input("BaseColor", "Shared", PbrMapKind.BaseColor),
             Input("Overlay_BaseColor", "Shared", PbrMapKind.BaseColor),
             Input("PM_Diffuse", "Shared", PbrMapKind.BaseColor)], "BaseColor");
        AssertSelection("unambiguous compatibility aliases still work",
            [Input("PM_Diffuse", "Body", PbrMapKind.BaseColor),
             Input("PM_Normals", "Normal", PbrMapKind.Normal)], "PM_Diffuse", "PM_Normals");
        AssertSelection("single unknown resource remains a compatibility fallback",
            [Input("Tex", "Unique", null), Input("Unique", "Unique", null)], "Tex");
        AssertSelection("multiple unknown textures are not an alphabetical BaseColor",
            [Input("A", "UnknownA", null), Input("B", "UnknownB", null)]);
        AssertSelection("all-cubemap material has no 2D PBR bindings",
            [Input("PM_Diffuse", "Cube", PbrMapKind.BaseColor) with { SourceTextureIsCubeMap = true }]);

        var classify = typeof(FModelMaterialProjectionBuilder).GetMethod("TryClassifyMap",
            BindingFlags.Static | BindingFlags.NonPublic, null,
            [typeof(string), typeof(PbrMapKind).MakeByRefType()], null)!;
        object[] classifyArgs = ["PetalDetailMap", default(PbrMapKind)];
        if (!(bool)classify.Invoke(null, classifyArgs)! || (PbrMapKind)classifyArgs[1] != PbrMapKind.BaseColor)
            throw new InvalidOperationException("Known upstream primary names must not be classified only via PM_* aliases.");
        AssertSelection("upstream primary detail-looking name is not discarded",
            [Input("PetalDetailMap", "Petal", PbrMapKind.BaseColor),
             Input("PM_Diffuse", "Wrong", PbrMapKind.BaseColor)], "PetalDetailMap");
        AssertSelection("subsurface/specular/emissive color is not surface BaseColor",
            [Input("PM_Diffuse", "Body", PbrMapKind.BaseColor),
             Input("SubSurfaceColor", "Subsurface", PbrMapKind.BaseColor),
             Input("SpecularColorMap", "Specular", PbrMapKind.BaseColor),
             Input("EmissiveColor", "Emissive", PbrMapKind.BaseColor)], "PM_Diffuse");
        AssertSelection("unsupported auxiliary color has no arbitrary fallback",
            [Input("SubSurfaceColor", "Auxiliary", PbrMapKind.BaseColor),
             Input("PM_Diffuse", "Auxiliary", PbrMapKind.BaseColor)]);
        AssertSelection("game-specific official primary name overrides generic auxiliary heuristic",
            [Input("PBREmissiveTex", "GameNormal", PbrMapKind.Normal)], "PBREmissiveTex");
        var failedPrimary = primary with { Outputs = [], ExportMetadataAvailable = false };
        var selected = Select([failedPrimary, Input("PM_Diffuse", "AvailableButWrong", PbrMapKind.BaseColor)]);
        if (selected.Single().ParameterIdentity != "BaseColor")
            throw new InvalidOperationException("A failed primary export was silently replaced by an unrelated map.");
        foreach (var mode in new[] { FModelUeFormatMaterialMode.Linked, FModelUeFormatMaterialMode.Strict })
        {
            var projection = new FModelMaterialProjectionBuilder().Build("/Game/Mesh.Mesh", 1,
                [new FModelMaterialSlotInput(0, 0, "Body", "/Game/Material.Material", "Material.json", selected)],
                new FModelUeFormatPolicy(true, mode));
            var expected = mode == FModelUeFormatMaterialMode.Strict
                ? FModelUeFormatDiagnosticSeverity.Error : FModelUeFormatDiagnosticSeverity.Warning;
            if (!projection.Diagnostics.Any(item => item.Code == "texture.export.metadata" && item.Severity == expected))
                throw new InvalidOperationException("Primary preview failure bypassed Linked/Strict diagnostics.");
        }
        Console.WriteLine("PrimaryTextureSelectionChecks: 17 cases PASS (including reversed input order and Linked/Strict failure).");
    }

    private static IReadOnlyList<FModelTextureInput> Select(IReadOnlyList<FModelTextureInput> inputs) =>
        (IReadOnlyList<FModelTextureInput>)typeof(FModelMaterialProjectionBuilder)
            .GetMethod("SelectPrimaryPbrTextures", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [inputs])!;

    private static void AssertSelection(string label, FModelTextureInput[] inputs, params string[] expected)
    {
        foreach (var ordered in new[] { inputs, inputs.Reverse().ToArray() })
        {
            var actual = Select(ordered).Select(input => input.ParameterIdentity).ToArray();
            if (!actual.SequenceEqual(expected))
                throw new InvalidOperationException($"{label}: [{string.Join(", ", actual)}] != [{string.Join(", ", expected)}]");
        }
    }

    private static FModelTextureInput Input(string name, string asset, PbrMapKind? kind) =>
        new(name, $"/Game/Textures/{asset}.{asset}", $"Textures/{asset}.png")
        {
            MapKind = kind,
            SourceTextureIsNormalMap = kind == PbrMapKind.Normal,
            Outputs = [new FModelTextureOutputProjection(asset, $"Textures/{asset}.png", 4, new byte[32])],
            ExportMetadataAvailable = true
        };
}
