using System.Reflection;
using CUE4Parse.FModelUEFormat.ExportPipeline.Diagnostics;
using CUE4Parse.FModelUEFormat.ExportPipeline.Formats;
using CUE4Parse.FModelUEFormat.ExportPipeline.Materials;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.FModelUEFormat.ExportPipeline.Writers;
using CUE4Parse.UE4.Writers;
using CUE4Parse_Conversion.Writers.UEFormat.Structs;
using CUE4Parse.GameTypes.Nascar.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.GeometryCollection;
using CUE4Parse.UE4.Assets.Exports.Houdini;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UeFormat.UEModel;
using CUE4Parse_Conversion.Options;
using FModel.Services;

// Anonymous protocol/diagnostic boundaries. These are not shader visual tests.
internal static class MaterialMappingGuardrailChecks
{
    internal static void Run()
    {
        foreach (var name in new[] { "ORM", "Orm", "Base_ORM", "BASE-ORM", "OcclusionRoughnessMetallicTexture", "RGB[AO/R/Metallic]_Texture" })
        {
            var projection = Build(name, FModelUeFormatMaterialMode.Strict);
            Check(projection.IsComplete && projection.Textures.Count == 3, "Declared ORM was incorrectly skipped: " + name);
        }
        foreach (var name in new[] { "Base_SRO", "MRA", "MRO", "RMA", "AO", "SpecularMasks", "PM_SpecularMasks", "Special_ORM_GamePacking" })
        {
            foreach (var mode in new[] { FModelUeFormatMaterialMode.Linked, FModelUeFormatMaterialMode.Strict })
            {
                var projection = Build(name, mode);
                Check(projection.Materials.Count == 1 && projection.Textures.Count == 2 && projection.TextureResources.Count == 2,
                    "Unverified mask leaked into resources or removed BaseColor/Normal: " + name);
                var diagnostic = projection.Diagnostics.Single(d => d.Code == "texture.orm.layout-unverified");
                Check(diagnostic.MaterialSlot == "Body" && diagnostic.TextureParameter == name && diagnostic.Recovery is not null,
                    "Mask diagnostic lost slot/parameter/recovery context.");
                Check(diagnostic.Severity == (mode == FModelUeFormatMaterialMode.Strict
                        ? FModelUeFormatDiagnosticSeverity.Error : FModelUeFormatDiagnosticSeverity.Warning)
                    && projection.IsComplete == (mode == FModelUeFormatMaterialMode.Linked), "Wrong Linked/Strict boundary.");
                Check(!projection.Diagnostics.Any(d => d.Code == "texture.export.metadata"), "Mask warning became an unrelated preview failure.");
                CheckFormatBoundary(projection, mode);
            }
        }
        var failedMask = Input("Base_SRO", PbrMapKind.Orm) with { Outputs = [], ExportMetadataAvailable = false };
        var precedence = BuildInputs([failedMask], FModelUeFormatMaterialMode.Strict);
        Check(precedence.Diagnostics.Single().Code == "texture.orm.layout-unverified", "Unknown layout must precede output-metadata checks.");
        Check(!Build("MRA", FModelUeFormatMaterialMode.Strict, emitLinks: false).Diagnostics.Any(d => d.Code == "texture.orm.layout-unverified"),
            "Texture-link-disabled export must not enforce layout.");
        Check(!Build("MRA", FModelUeFormatMaterialMode.Disabled).Diagnostics.Any(d => d.Code == "texture.orm.layout-unverified"),
            "Material-disabled export must not enforce layout.");

        var select = typeof(FModelMaterialProjectionBuilder).GetMethod("SelectPrimaryPbrTextures", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var ordered in new[] {
            new[] { Input("SpecularMasks", PbrMapKind.Orm), Input("ORM", PbrMapKind.Orm) },
            new[] { Input("ORM", PbrMapKind.Orm), Input("SpecularMasks", PbrMapKind.Orm) } })
        {
            var selected = (IReadOnlyList<FModelTextureInput>)select.Invoke(null, [ordered])!;
            Check(selected.Single().ParameterIdentity == "SpecularMasks", "Unsafe primary was replaced by another mask.");
            var projection = BuildInputs(selected, FModelUeFormatMaterialMode.Linked);
            Check(projection.Textures.Count == 0 && projection.Diagnostics.Any(d => d.Code == "texture.primary.selected"),
                "Primary explanation was lost when unsafe ORM binding was skipped.");
        }
        CheckEligibility();
        Console.WriteLine("MaterialMappingGuardrailChecks: declared/unknown layouts, Linked/Strict format rejection, policy exemptions, no-substitution, standard-only eligibility PASS.");
    }

    private static void CheckEligibility()
    {
        UObject[] standardOnly = [new UHoudiniStaticMesh { Name = "Houdini" }, new UIRMesh { Name = "IR" }];
        foreach (var export in standardOnly)
        {
            foreach (var mode in new[] { FModelUeFormatMaterialMode.Linked, FModelUeFormatMaterialMode.Strict })
            {
                var diagnostic = FModelV3MeshEligibility.StandardOnlyDiagnostic(export, EMeshFormat.UEFormat, new(true, mode));
                Check(diagnostic?.Code == "material.type.standard-only" && diagnostic.Severity == FModelUeFormatDiagnosticSeverity.Warning
                    && diagnostic.Message.Contains("Strict material completeness does not apply"), "Standard-only export lost its material-scope warning.");
            }
            foreach (var format in new[] { EMeshFormat.ActorX, EMeshFormat.Gltf2, EMeshFormat.USD })
                Check(FModelV3MeshEligibility.StandardOnlyDiagnostic(export, format, new(true, FModelUeFormatMaterialMode.Strict)) is null,
                    "Non-UEFormat export emitted a material warning.");
            Check(FModelV3MeshEligibility.StandardOnlyDiagnostic(export, EMeshFormat.UEFormat, FModelUeFormatPolicy.Disabled) is null,
                "Disabled pipeline emitted a warning.");
            Check(FModelV3MeshEligibility.StandardOnlyDiagnostic(export, EMeshFormat.UEFormat, new(true, FModelUeFormatMaterialMode.Disabled)) is null,
                "Disabled materials emitted a warning.");
        }
        UObject[] supported = [new UStaticMesh { Name = "Static" }, new USkeletalMesh { Name = "Skeletal" }, new UGeometryCollection { Name = "Geometry" }];
        foreach (var export in supported)
            Check(FModelV3MeshEligibility.SupportsMaterialProjection(export)
                && FModelV3MeshEligibility.StandardOnlyDiagnostic(export, EMeshFormat.UEFormat, new(true, FModelUeFormatMaterialMode.Strict)) is null,
                "Supported mesh emitted a standard-only warning.");
        Check(FModelV3MeshEligibility.StandardOnlyDiagnostic(new USkeleton { Name = "Skeleton" }, EMeshFormat.UEFormat,
            new(true, FModelUeFormatMaterialMode.Strict)) is null, "Skeleton-only export emitted an irrelevant material warning.");
    }

    private static void CheckFormatBoundary(FModelMaterialProjection projection, FModelUeFormatMaterialMode mode)
    {
        var format = new FModelUeFormatMeshFormat(new(true, mode));
        var build = typeof(FModelUeFormatMeshFormat).GetMethod("TryBuildProjection", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            var result = build.Invoke(format, ["/Game/Mesh.Mesh", "fixture", 1, 1, true, (Func<FModelMaterialProjection>)(() => projection)]);
            Check(mode == FModelUeFormatMaterialMode.Linked && ReferenceEquals(result, projection), "Strict format accepted an incomplete material projection.");
            var attributes = new FDataAttributeSet();
            new FModelV10AttributeWriter().WriteProjection(attributes, projection, 1);
            using var archive = new FArchiveWriter();
            attributes.Serialize(archive);
            Check(archive.GetBuffer().Length > 0, "Safe partial Linked mapping did not serialize.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            Check(mode == FModelUeFormatMaterialMode.Strict, "Linked format rejected a safe partial mapping.");
        }
    }

    private static FModelMaterialProjection Build(string mask, FModelUeFormatMaterialMode mode, bool emitLinks = true) =>
        BuildInputs([Input("BaseColor", PbrMapKind.BaseColor), Input("Normal", PbrMapKind.Normal), Input(mask, PbrMapKind.Orm)], mode, emitLinks);

    private static FModelMaterialProjection BuildInputs(IReadOnlyList<FModelTextureInput> inputs, FModelUeFormatMaterialMode mode, bool emitLinks = true) =>
        new FModelMaterialProjectionBuilder().Build("/Game/Mesh.Mesh", 1,
            [new FModelMaterialSlotInput(0, 0, "Body", "/Game/Material.Material", "Material.json", inputs)], new(true, mode, EmitTextureLinks: emitLinks));

    private static FModelTextureInput Input(string name, PbrMapKind kind) =>
        new(name, "/Game/Textures/" + name + ".Texture", "Textures/" + name + ".png")
        {
            MapKind = kind, ExportMetadataAvailable = true,
            Outputs = [new FModelTextureOutputProjection(name, "Textures/" + name + ".png", 4, new byte[32])]
        };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
