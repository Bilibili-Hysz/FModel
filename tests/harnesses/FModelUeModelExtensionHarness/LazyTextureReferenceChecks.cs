using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using CUE4Parse.FModelUEFormat.ExportPipeline.Diagnostics;
using CUE4Parse.FModelUEFormat.ExportPipeline.Materials;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.GameTypes.Nascar.Assets.Exports;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Houdini;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UeFormat.UEModel;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Options;

// Synthetic API migration checks; these do not represent a real encoded texture
// or package-backed export. Texture preview failure is deliberate in this fixture.
internal static class LazyTextureReferenceChecks
{
    private static readonly FModelUeFormatPolicy Policy = new(true, FModelUeFormatMaterialMode.Linked);
    private static readonly MethodInfo BuildMethod = typeof(FModelMaterialProjectionBuilder)
        .GetMethod("BuildPrimaryTextureInputs", BindingFlags.Static | BindingFlags.NonPublic)!;

    internal static void Run()
    {
        var main = new Reference(new UTexture2D { Name = "Main" });
        var selected = Build(new() { ["BaseColor"] = main, ["PM_Diffuse"] = main });
        Check(main.Loads == 1 && selected.Single().ParameterIdentity == "BaseColor", "Duplicate aliases loaded twice or outranked the primary.");

        var auxiliary = new Reference(new UTexture2D { Name = "Overlay" });
        selected = Build(new() { ["BaseColor"] = main, ["Overlay_BaseColor"] = auxiliary });
        Check(auxiliary.Loads == 0 && selected.Single().ParameterIdentity == "BaseColor", "Auxiliary texture data was eagerly loaded.");
        selected = Build(new() { ["BaseColor"] = main, ["Overlay_BaseColor"] = auxiliary, ["PM_Diffuse"] = auxiliary });
        Check(selected.Single().ParameterIdentity == "BaseColor", "Lazy aliases reintroduced the excluded overlay.");

        var cube = new Reference(new UTextureCube { Name = "Cube" });
        selected = Build(new() { ["BaseColor"] = main, ["PM_Diffuse"] = cube });
        Check(selected.Single().ParameterIdentity == "BaseColor", "A loaded cube became the surface color.");

        var missing = new Reference(null, "/Game/Missing.Missing");
        selected = Build(new() { ["BaseColor"] = missing, ["PM_Diffuse"] = main });
        Check(selected.Single().ParameterIdentity == "BaseColor" && selected[0].ExportMetadataAvailable == false,
            "An unresolved primary was silently dropped or replaced.");
        foreach (var mode in new[] { FModelUeFormatMaterialMode.Linked, FModelUeFormatMaterialMode.Strict })
        {
            var projection = new FModelMaterialProjectionBuilder().Build("/Game/Mesh.Mesh", 1,
                [new FModelMaterialSlotInput(0, 0, "Body", "/Game/Material.Material", "Material.json", selected)],
                new FModelUeFormatPolicy(true, mode));
            var severity = mode == FModelUeFormatMaterialMode.Strict
                ? FModelUeFormatDiagnosticSeverity.Error : FModelUeFormatDiagnosticSeverity.Warning;
            Check(projection.Diagnostics.Any(d => d.Code == "texture.export.metadata" && d.Severity == severity),
                "Lazy load failure bypassed Linked/Strict diagnostics.");
        }

        selected = Build(new() { ["BaseColor"] = new ThrowingReference(false) });
        Check(selected.Single().ExportMetadataAvailable == false, "A throwing reference lost its failed primary candidate.");

        using var previewSession = new ExportSession { Extension = new NoopExportSessionExtension() };
        var preview = new FModelTextureExportPreview(previewSession);
        selected = Build(new() { ["BaseColor"] = main, ["ExtraDiffuse"] = new Reference(new UTexture2D { Name = "Other" }),
            ["PM_Diffuse"] = cube, ["Overlay_BaseColor"] = auxiliary }, preview);
        Check(selected.Single().ParameterIdentity == "BaseColor" && previewSession.TotalQueued == 1,
            "Rejected candidates were added to the texture preview queue.");

        using var maskSession = new ExportSession { Extension = new NoopExportSessionExtension() };
        var maskPreview = new FModelTextureExportPreview(maskSession);
        selected = Build(new() { ["SpecularMasks"] = new Reference(new UTexture2D { Name = "UnknownMask" }),
            ["ORM"] = new Reference(new UTexture2D { Name = "KnownMask" }) }, maskPreview);
        Check(selected.Single().ParameterIdentity == "SpecularMasks" && maskSession.TotalQueued == 0,
            "An unverified selected mask queued extra preview encoding or substituted the lower-priority ORM.");

        var normal = new UTexture2D { Name = "N" };
        typeof(UTexture).GetProperty(nameof(UTexture.CompressionSettings))!.SetValue(normal, TextureCompressionSettings.TC_Normalmap);
        selected = Build(new() { ["UnknownTexture"] = new Reference(normal) });
        Check(selected.Single().MapKind == PbrMapKind.Normal, "Resolved normal-map metadata was lost in lazy classification.");

        try
        {
            Build(new() { ["BaseColor"] = new ThrowingReference(true) });
            throw new InvalidOperationException("Projection swallowed cancellation.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException) { }

        using var newExporterSession = new ExportSession();
        newExporterSession.Add(new UHoudiniAsset { Name = "Houdini" });
        newExporterSession.Add(new UIRMesh { Name = "IRMesh" });
        Check(newExporterSession.TotalQueued == 2, "Deferred session registration blocked newly supported official exporters.");
        Console.WriteLine("LazyTextureReferenceChecks: existing 8 cases + unverified-mask preview suppression PASS; new exporter queue: IRMesh + Houdini PASS.");
    }

    private static IReadOnlyList<FModelTextureInput> Build(Dictionary<string, ILoadableObject> references,
        FModelTextureExportPreview? preview = null) =>
        (IReadOnlyList<FModelTextureInput>)BuildMethod.Invoke(null, [references, new ExportOptions(), Policy, preview, null])!;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private sealed class Reference(UTexture? texture, string? identity = null) : ResolvedObject(null!)
    {
        public int Loads { get; private set; }
        public override FName Name => new(identity ?? texture!.Name);
        public override Lazy<UObject>? Object
        {
            get { Loads++; return texture is null ? null : new Lazy<UObject>(() => texture); }
        }
    }
    private sealed class ThrowingReference(bool cancel) : ILoadableObject
    {
        public Type? GetObjectType() => typeof(UTexture2D);
        public UObject? Load()
        {
            if (cancel) throw new OperationCanceledException();
            throw new InvalidOperationException("Fixture load failure.");
        }
        public bool TryLoad([MaybeNullWhen(false)] out UObject export) { export = null; Load(); return false; }
        public Task<UObject?> LoadAsync() => Task.FromResult(Load());
        public Task<UObject?> TryLoadAsync() => LoadAsync();
    }
}
