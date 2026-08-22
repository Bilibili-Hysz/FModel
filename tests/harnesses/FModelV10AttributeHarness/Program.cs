using System.Security.Cryptography;
using CUE4Parse.FModelUEFormat.ExportPipeline.Materials;
using CUE4Parse.FModelUEFormat.ExportPipeline.Writers;
using CUE4Parse.UeFormat.UEModel;
using CUE4Parse.UE4.Writers;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Writers.UEFormat;
using CUE4Parse_Conversion.Writers.UEFormat.Enums;
using CUE4Parse_Conversion.Writers.UEFormat.Structs;

// This harness intentionally has no Unreal asset dependency. It exercises the
// exact V10 attribute callback used by the FModel mesh adapter and emits
// producer fixtures for the Blender contract tests.
var outputDirectory = args.Length == 1
    ? Path.GetFullPath(args[0])
    : Path.Combine(AppContext.BaseDirectory, "output");
Directory.CreateDirectory(outputDirectory);

var projection = CreateProjection();
var identity = new FModelSkeletonIdentity(
    "/CodeWorld/Content/Characters/Heroes/SKEL_FM_Base.SKEL_FM_Base",
    null,
    SHA256.HashData("fmodel-v10-contract-skeleton"u8));

var outputs = new List<string>();
foreach (var compression in new[]
         {
             EFileCompressionFormat.None,
             EFileCompressionFormat.GZIP,
             EFileCompressionFormat.ZSTD
         })
{
    var options = new ExportOptions(compressionFormat: compression);
    var model = new SyntheticModel(
        "SM_FModelV10Contract",
        "/CodeWorld/Content/Characters/Heroes/SM_FModelV10Contract.SM_FModelV10Contract",
        options,
        projection,
        identity);
    using var archive = new FArchiveWriter();
    model.Save(archive);
    var output = Path.Combine(outputDirectory, $"fmodel_v10_{compression.ToString().ToLowerInvariant()}.uemodel");
    File.WriteAllBytes(output, archive.GetBuffer());
    outputs.Add(output);
}

Console.WriteLine("FModel V10 producer fixtures:");
foreach (var output in outputs)
    Console.WriteLine($"  {output}");

return 0;

static FModelMaterialProjection CreateProjection()
{
    var resources = new[]
    {
        Resource("texture.basecolor", "../Textures/T_FModelV10_BaseColor.png", 0x11),
        Resource("texture.normal", "../Textures/T_FModelV10_Normal.png", 0x22),
        Resource("texture.orm", "../Textures/T_FModelV10_Orm.png", 0x33)
    };
    var textures = new[]
    {
        Link("BaseColor", "texture.basecolor", PbrMapKind.BaseColor, true),
        Link("Normal", "texture.normal", PbrMapKind.Normal, false),
        Link("Orm", "texture.orm", PbrMapKind.Orm, false)
    };
    return new FModelMaterialProjection(
        "/CodeWorld/Content/Characters/Heroes/SM_FModelV10Contract.SM_FModelV10Contract",
        [new FModelMaterialLinkProjection(
            0,
            0,
            "Body",
            "/CodeWorld/Content/Materials/M_FModelV10_Body.M_FModelV10_Body",
            "../Materials/M_FModelV10_Body.json")],
        textures,
        Array.Empty<CUE4Parse.FModelUEFormat.ExportPipeline.Diagnostics.FModelUeFormatPipelineDiagnostic>(),
        resources);

    static FModelTextureResourceProjection Resource(string id, string uri, byte seed) => new(
        id,
        $"/CodeWorld/Content/Textures/{id}.{id}",
        uri,
        4,
        Enumerable.Repeat(seed, 32).ToArray(),
        LocationMode: FModelTextureResourceLocation.Relative);

    static FModelTextureLinkProjection Link(
        string parameter,
        string resourceId,
        PbrMapKind kind,
        bool isSrgb) => new(
        0,
        parameter,
        $"/CodeWorld/Content/Textures/{resourceId}.{resourceId}",
        $"../Textures/{resourceId}.png",
        resourceId,
        kind,
        isSrgb,
        "Body");
}

sealed class SyntheticModel : UEFormatExport
{
    private readonly FModelMaterialProjection projection;
    private readonly FModelSkeletonIdentity identity;

    protected override string Identifier => "UEMODEL";

    public SyntheticModel(
        string objectName,
        string objectPath,
        ExportOptions options,
        FModelMaterialProjection projection,
        FModelSkeletonIdentity identity)
        : base(objectName, objectPath, options)
    {
        this.projection = projection;
        this.identity = identity;
        WriteRoot(root =>
        {
            // A zero-length standard LODS attribute is valid and lets Blender
            // exercise standard parsing and unknown FMODEL_* skipping without
            // inventing a synthetic mesh topology.
            root.AddAttribute("LODS", attribute => attribute.Write(0));
            new FModelV10AttributeWriter().WriteProjection(root, projection, 1);
            FModelSkeletonExtensionWriter.WriteModelIdentity(root, identity);
        });
    }
}
