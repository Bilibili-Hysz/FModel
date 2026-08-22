using System.Security.Cryptography;
using CUE4Parse.FModelUEFormat.ExportPipeline.Materials;
using CUE4Parse.FModelUEFormat.ExportPipeline.Writers;
using CUE4Parse.UeFormat.UEModel;
using CUE4Parse.UE4.Writers;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Meshes;
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
WriteProjectedTextures(outputDirectory, projection);
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
    var output = Path.Combine(
        outputDirectory,
        "models",
        $"fmodel_v10_{compression.ToString().ToLowerInvariant()}.uemodel");
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
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
        Resource("texture.basecolor", "../Textures/T_FModelV10_BaseColor.png"),
        Resource("texture.normal", "../Textures/T_FModelV10_Normal.png"),
        Resource("texture.orm", "../Textures/T_FModelV10_Orm.png")
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

    static FModelTextureResourceProjection Resource(string id, string uri)
    {
        var data = Png1x1();
        return new FModelTextureResourceProjection(
            id,
            $"/CodeWorld/Content/Textures/{id}.{id}",
            uri,
            data.Length,
            SHA256.HashData(data),
            LocationMode: FModelTextureResourceLocation.Relative);
    }

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

static byte[] Png1x1() => Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/5ncLrgAAAABJRU5ErkJggg==");

static void WriteProjectedTextures(string outputDirectory, FModelMaterialProjection projection)
{
    var modelDirectory = Path.Combine(outputDirectory, "models");
    foreach (var resource in projection.TextureResources)
    {
        var outputPath = Path.GetFullPath(Path.Combine(
            modelDirectory,
            resource.LogicalResourceUri.Replace('/', Path.DirectorySeparatorChar)));
        var outputRoot = Path.GetFullPath(outputDirectory) + Path.DirectorySeparatorChar;
        if (!outputPath.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Fixture texture escaped output root: {resource.LogicalResourceUri}");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(outputPath, Png1x1());
    }
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
            WriteTriangleLod(root);
            new FModelV10AttributeWriter().WriteProjection(root, projection, 1);
            FModelSkeletonExtensionWriter.WriteModelIdentity(root, identity);
        });
    }

    private static void WriteTriangleLod(FDataAttributeSet root)
    {
        root.AddAttribute("LODS", attribute => attribute.WriteArray(
            new[] { "LOD0" },
            (writer, _) =>
            {
                writer.WriteFString("LOD0");
                writer.WriteAttributes(attrs =>
                {
                    attrs.AddAttribute("VERTICES", data => data.WriteArray(
                        new[]
                        {
                            new FVector(0, 0, 0),
                            new FVector(1, 0, 0),
                            new FVector(0, 1, 0),
                        },
                        (vertexWriter, vertex) => vertex.Serialize(vertexWriter)));
                    attrs.AddAttribute("NORMALS", data => data.WriteArray(
                        new[]
                        {
                            new FVector4(0, 0, 1, 1),
                            new FVector4(0, 0, 1, 1),
                            new FVector4(0, 0, 1, 1),
                        },
                        (normalWriter, normal) =>
                        {
                            normalWriter.Write(normal.W);
                            new FVector(normal.X, normal.Y, normal.Z).Serialize(normalWriter);
                        }));
                    attrs.AddAttribute("TEXCOORDS", data => data.WriteArray(
                        new[] { "UV0" },
                        (uvSetWriter, uvSetName) =>
                        {
                            uvSetWriter.WriteFString(uvSetName);
                            uvSetWriter.WriteArray(
                                new[]
                                {
                                    new FMeshUVFloat(0, 0),
                                    new FMeshUVFloat(1, 0),
                                    new FMeshUVFloat(0, 1),
                                },
                                (uvWriter, uv) => uv.Serialize(uvWriter));
                        }));
                    attrs.AddAttribute("INDICES", data => data.WriteArray(
                        new uint[] { 0, 1, 2 },
                        (indexWriter, index) => indexWriter.Write(index)));
                    attrs.AddAttribute("MATERIALS", data => data.WriteArray(
                        new[] { (Name: "Body", Path: "/CodeWorld/Content/Materials/M_FModelV10_Body.M_FModelV10_Body", First: 0, Faces: 1) },
                        (materialWriter, material) =>
                        {
                            materialWriter.WriteFString(material.Name);
                            materialWriter.WriteFString(material.Path);
                            materialWriter.Write(material.First);
                            materialWriter.Write(material.Faces);
                        }));
                });
            }));
    }
}
