using System.Linq;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Readers;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Exporters;
using CUE4Parse_Conversion.Options;
using CUE4Parse.FModelUEFormat.ExportPipeline.Diagnostics;
using CUE4Parse.FModelUEFormat.ExportPipeline.Materials;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.FModelUEFormat.ExportPipeline.Writers;
using CUE4Parse.UeFormat.UEModel;
using CUE4Parse.UE4.Writers;
using CUE4Parse_Conversion.Writers.UEFormat;
using CUE4Parse_Conversion.Writers.UEFormat.Enums;
using CUE4Parse_Conversion.Writers.UEFormat.Structs;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse_Conversion.Formats.Meshes;

using var payload = new FArchiveWriter();
using (var lods = new FDataChunk("LODS", 1))
{
    using var lod = new FStaticDataChunk("LOD0");
    using (var vertices = new FDataChunk("VERTICES", 1)) { vertices.Write(0f); vertices.Write(0f); vertices.Write(0f); vertices.Serialize(lod); }
    using (var normals = new FDataChunk("NORMALS", 1)) { normals.Write(1f); normals.Write(0f); normals.Write(0f); normals.Write(1f); normals.Serialize(lod); }
    using (var tangents = new FDataChunk("TANGENTS", 1)) { tangents.Write(1f); tangents.Write(0f); tangents.Write(0f); tangents.Serialize(lod); }
    using (var uvs = new FDataChunk("TEXCOORDS", 1)) { uvs.Write(1); uvs.Write(0f); uvs.Write(0f); uvs.Serialize(lod); }
    using (var indices = new FDataChunk("INDICES", 3)) { indices.Write(0); indices.Write(0); indices.Write(0); indices.Serialize(lod); }
    using (var materials = new FDataChunk("MATERIALS", 1)) { materials.WriteFString("Body"); materials.WriteFString("CodeWorld/Content/M_Body.M_Body"); materials.Write((uint)0); materials.Write((uint)1); materials.Serialize(lod); }
    lod.Serialize(lods);
    lods.Serialize(payload);
}

using var standard = new FArchiveWriter();
var header = new FUEFormatHeader("UEMODEL", "SM_Test", EFileCompressionFormat.None);
header.Serialize(standard);
standard.Write(payload.GetBuffer());

var projection = new FModelMaterialProjection(
    "/Game/SM_Test.SM_Test",
    [new FModelMaterialLinkProjection(0, 0, "Body", "CodeWorld/Content/M_Body.M_Body", "materials/body.json")],
    Array.Empty<FModelTextureLinkProjection>(),
    Array.Empty<FModelUeFormatPipelineDiagnostic>());
var composer = new FModelUeModelExtensionComposer();
var output = composer.AppendMaterialLinks(standard.GetBuffer(), projection, 1);
var inspection = UeModelInspector.Inspect(output);

var textureProjection = new FModelMaterialProjection(
    "/Game/SM_Test.SM_Test",
    projection.Materials,
    [new FModelTextureLinkProjection(
        0,
        "BaseColor",
        "/Game/T_BC.T_BC",
        "textures/T_BC.png",
        "tex.base",
        PbrMapKind.BaseColor,
        true,
        "Body")],
    Array.Empty<FModelUeFormatPipelineDiagnostic>(),
    [new FModelTextureResourceProjection(
        "tex.base",
        "/Game/T_BC.T_BC",
        "textures/T_BC.png",
        4,
        [0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11,
         0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11,
         0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11,
         0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11])]);
var textureOutput = composer.AppendProjection(standard.GetBuffer(), textureProjection, 1);
var textureInspection = UeModelInspector.Inspect(textureOutput);

var relativeProjection = new FModelMaterialProjection(
    "/Game/SM_Test.SM_Test",
    [new FModelMaterialLinkProjection(0, 0, "Body", "CodeWorld/Content/M_Body.M_Body", "../Material/body.json")],
    [new FModelTextureLinkProjection(
        0,
        "BaseColor",
        "/Game/T_BC.T_BC",
        "../Texture/T_BC.png",
        "tex.relative",
        PbrMapKind.BaseColor,
        true,
        "Body")],
    Array.Empty<FModelUeFormatPipelineDiagnostic>(),
    [new FModelTextureResourceProjection(
        "tex.relative",
        "/Game/T_BC.T_BC",
        "../Texture/T_BC.png",
        4,
        Enumerable.Repeat((byte)0x44, 32).ToArray(),
        LocationMode: FModelTextureResourceLocation.Relative)]);
var relativeOutput = composer.AppendProjection(standard.GetBuffer(), relativeProjection, 1);
var relativeInspection = UeModelInspector.Inspect(relativeOutput);

var duplicateTextureProjection = new FModelMaterialProjection(
    "/Game/SM_Test.SM_Test",
    projection.Materials,
    [new FModelTextureLinkProjection(0, "BaseColor", "/Game/T_BC.T_BC", "textures/T_BC.png", "tex.base", PbrMapKind.BaseColor, true, "Body"),
     new FModelTextureLinkProjection(0, "Diffuse", "/Game/T_BC.T_BC", "textures/T_BC.png", "tex.base", PbrMapKind.BaseColor, true, "Body")],
    Array.Empty<FModelUeFormatPipelineDiagnostic>(),
    textureProjection.TextureResources);
var duplicateTextureOutput = composer.AppendProjection(standard.GetBuffer(), duplicateTextureProjection, 1);
var duplicateTextureInspection = UeModelInspector.Inspect(duplicateTextureOutput);

var duplicateHashA = Enumerable.Repeat((byte)0x33, 32).ToArray();
var duplicateHashB = Enumerable.Repeat((byte)0x33, 32).ToArray();
var builder = new FModelMaterialProjectionBuilder();
var duplicateResourceProjection = builder.Build("/Game/SM_Test.SM_Test", 1,
    [new FModelMaterialSlotInput(0, 0, "Body", "CodeWorld/Content/M_Body.M_Body", "materials/body.json",
        [new FModelTextureInput("BaseColor", "/Game/T_BC.T_BC", "textures/T_BC.png")
        {
            Outputs = [new FModelTextureOutputProjection("tex.shared", "textures/T_BC.png", 4, duplicateHashA, OutputOrdinal: 0)],
            ExportMetadataAvailable = true,
            MapKind = PbrMapKind.BaseColor,
            IsSrgb = true
        }]),
     new FModelMaterialSlotInput(1, 0, "BodyCopy", "CodeWorld/Content/M_Body.M_Body", "materials/body-copy.json",
        [new FModelTextureInput("BaseColor", "/Game/T_BC.T_BC", "textures/T_BC.png")
        {
            Outputs = [new FModelTextureOutputProjection("tex.shared", "textures/T_BC.png", 4, duplicateHashB, OutputOrdinal: 0)],
            ExportMetadataAvailable = true,
            MapKind = PbrMapKind.BaseColor,
            IsSrgb = true
        }])],
    new FModelUeFormatPolicy(true, FModelUeFormatMaterialMode.Strict));

var mipProjection = builder.Build("/Game/SM_Test.SM_Test", 1,
    [new FModelMaterialSlotInput(0, 0, "Body", "CodeWorld/Content/M_Body.M_Body", "materials/body.json",
        [new FModelTextureInput("BaseColor", "/Game/T_BC.T_BC", "textures/T_BC.png")
        {
            Outputs = [
                new FModelTextureOutputProjection("tex.mip2", "textures/T_BC_MIP2.png", 4, Enumerable.Repeat((byte)0x22, 32).ToArray(), OutputOrdinal: 0),
                new FModelTextureOutputProjection("tex.mip10", "textures/T_BC_MIP10.png", 4, Enumerable.Repeat((byte)0x10, 32).ToArray(), OutputOrdinal: 1)
            ],
            ExportMetadataAvailable = true,
            MapKind = PbrMapKind.BaseColor,
            IsSrgb = true
        }])],
    new FModelUeFormatPolicy(true, FModelUeFormatMaterialMode.Strict));

var linkedProjection = builder.Build("/Game/SM_Test.SM_Test", 1,
    [new FModelMaterialSlotInput(0, 0, "Body", "CodeWorld/Content/M_Body.M_Body", "materials/body.json",
        [new FModelTextureInput("BaseColor", "/Game/T_BC.T_BC", "textures/T_BC.png")])],
    new FModelUeFormatPolicy(true, FModelUeFormatMaterialMode.Linked));
var strictProjection = builder.Build("/Game/SM_Test.SM_Test", 1,
    [new FModelMaterialSlotInput(0, 0, "Body", null, "../unsafe.json")],
    new FModelUeFormatPolicy(true, FModelUeFormatMaterialMode.Strict));

var strictErrors = strictProjection.Diagnostics.Count(diagnostic => diagnostic.Severity == FModelUeFormatDiagnosticSeverity.Error);


var queueOutputRoot = Path.Combine(Path.GetTempPath(), "fmodel-export-session-requeue-" + Guid.NewGuid().ToString("N"));
var queueSession = new ExportSession { MaxDegreeOfParallelism = 1 };
var removedExporter = new QueueHarnessExporter("Game/Queue/Test.asset", 0x11);
var replacementExporter = new QueueHarnessExporter("Game/Queue/Test.asset", 0x22);
queueSession.AddOrGet(removedExporter);
if (!queueSession.Remove(removedExporter.ObjectPath))
    return 1;
queueSession.AddOrGet(replacementExporter);
IReadOnlyList<ExportResult> queueResults;
int queueByte;
try
{
    queueResults = await queueSession.RunAsync(queueOutputRoot, new ExportOptions());
    var queueOutputFile = Path.Combine(queueOutputRoot, "Game", "Queue", "Test.bin");
    queueByte = File.Exists(queueOutputFile) ? File.ReadAllBytes(queueOutputFile).Single() : -1;
}
finally
{
    if (Directory.Exists(queueOutputRoot))
        Directory.Delete(queueOutputRoot, recursive: true);
}

var sameInstanceOutputRoot = Path.Combine(Path.GetTempPath(), "fmodel-export-session-same-instance-requeue-" + Guid.NewGuid().ToString("N"));
var sameInstanceSession = new ExportSession { MaxDegreeOfParallelism = 1 };
var sameInstanceExporter = new QueueHarnessExporter("Game/Queue/Same.asset", 0x33);
sameInstanceSession.AddOrGet(sameInstanceExporter);
if (!sameInstanceSession.Remove(sameInstanceExporter.ObjectPath))
    return 1;
sameInstanceSession.AddOrGet(sameInstanceExporter);
IReadOnlyList<ExportResult> sameInstanceResults;
int sameInstanceByte;
try
{
    sameInstanceResults = await sameInstanceSession.RunAsync(sameInstanceOutputRoot, new ExportOptions());
    var sameInstanceOutputFile = Path.Combine(sameInstanceOutputRoot, "Game", "Queue", "Same.bin");
    sameInstanceByte = File.Exists(sameInstanceOutputFile) ? File.ReadAllBytes(sameInstanceOutputFile).Single() : -1;
}
finally
{
    if (Directory.Exists(sameInstanceOutputRoot))
        Directory.Delete(sameInstanceOutputRoot, recursive: true);
}



Console.WriteLine($"bytes={output.Length}; materialVersion={inspection.MaterialExtensionVersion}; slots={inspection.StandardMaterialSlots.Length}; links={inspection.MaterialLinks.Length}; linkedComplete={linkedProjection.IsComplete}; strictErrors={strictErrors}; sha={inspection.Sha256}");
Console.WriteLine($"textureBytes={textureOutput.Length}; textureVersion={textureInspection.TextureExtensionVersion}; resources={textureInspection.TextureResources.Length}; bindings={textureInspection.PbrTextureBindings.Length}; textureSha={textureInspection.Sha256}");
Console.WriteLine($"relativeTextureVersion={relativeInspection.TextureExtensionVersion}; relativeUri={relativeInspection.TextureResources.Single().LogicalResourceUri}; relativeLocation={relativeInspection.TextureResources.Single().LocationMode}; relativeMaterialUri={relativeInspection.MaterialLinks.Single().MaterialJsonUri}");
Console.WriteLine($"duplicateBindings={duplicateTextureInspection.PbrTextureBindings.Length}; duplicateResourcesComplete={duplicateResourceProjection.IsComplete}; mipResource={mipProjection.Textures.Single().TextureResourceId}");
Console.WriteLine($"queueReaddResults={queueResults.Count}; queueReaddByte={queueByte}; queueTotal={queueSession.TotalQueued}");
Console.WriteLine($"queueSameInstanceResults={sameInstanceResults.Count}; queueSameInstanceByte={sameInstanceByte}; queueSameInstanceTotal={sameInstanceSession.TotalQueued}");
if (inspection.MaterialExtensionVersion != 2 || inspection.MaterialLinks.Length != 1 || !linkedProjection.IsComplete || strictErrors == 0)
    return 1;
if (textureInspection.TextureExtensionVersion != 1 || textureInspection.TextureResources.Length != 1 || textureInspection.PbrTextureBindings.Length != 1)
    return 1;
if (relativeInspection.TextureExtensionVersion != 2
    || relativeInspection.TextureResources.Single().LogicalResourceUri != "../Texture/T_BC.png"
    || relativeInspection.TextureResources.Single().LocationMode != FModelTextureResourceLocation.Relative
    || relativeInspection.MaterialLinks.Single().MaterialJsonUri != "../Material/body.json")
    return 1;
if (duplicateTextureInspection.PbrTextureBindings.Length != 1 || !duplicateResourceProjection.IsComplete || duplicateResourceProjection.TextureResources.Count != 1)
    return 1;
if (mipProjection.Textures.Single().TextureResourceId != "tex.mip2")
    return 1;
if (queueResults.Count != 1 || !queueResults[0].Success || queueByte != 0x22 || queueSession.TotalQueued != 0)
    return 1;
if (sameInstanceResults.Count != 1 || !sameInstanceResults[0].Success || sameInstanceByte != 0x33 || sameInstanceSession.TotalQueued != 0)
    return 1;
return 0;

sealed class QueueHarnessExporter : ExporterBase
{
    private readonly byte value;

    public QueueHarnessExporter(string path, byte value)
        : base(new QueueHarnessGameFile(path), "QueueHarness") => this.value = value;

    protected override IReadOnlyList<ExportFile> BuildExportFiles(CancellationToken ct = default) =>
        [new ExportFile("bin", [value])];
}

sealed class QueueHarnessGameFile : GameFile
{
    public QueueHarnessGameFile(string path) : base(path, 0)
    {
    }

    public override bool IsEncrypted => false;
    public override CompressionMethod CompressionMethod => CompressionMethod.None;
    public override byte[] Read(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
    public override FArchive CreateReader(FByteBulkDataHeader? header = null) => throw new NotSupportedException();
}
