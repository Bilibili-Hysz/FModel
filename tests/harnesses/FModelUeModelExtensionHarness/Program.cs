using System.Linq;
using System.Reflection;
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
using CUE4Parse_Conversion.Writers.UEFormat.Structs;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse_Conversion.Formats.Meshes;

var projection = new FModelMaterialProjection(
    "/Game/SM_Test.SM_Test",
    [new FModelMaterialLinkProjection(0, 0, "Body", "CodeWorld/Content/M_Body.M_Body", "materials/body.json")],
    Array.Empty<FModelTextureLinkProjection>(),
    Array.Empty<FModelUeFormatPipelineDiagnostic>());

var textureProjection = new FModelMaterialProjection(
    "/Game/SM_Test.SM_Test",
    projection.Materials,
    [new FModelTextureLinkProjection(0, "BaseColor", "/Game/T_BC.T_BC", "textures/T_BC.png", "tex.base", PbrMapKind.BaseColor, true, "Body")],
    Array.Empty<FModelUeFormatPipelineDiagnostic>(),
    [new FModelTextureResourceProjection("tex.base", "/Game/T_BC.T_BC", "textures/T_BC.png", 4, Enumerable.Repeat((byte)0x11, 32).ToArray())]);

var relativeProjection = new FModelMaterialProjection(
    "/Game/SM_Test.SM_Test",
    [new FModelMaterialLinkProjection(0, 0, "Body", "CodeWorld/Content/M_Body.M_Body", "../Material/body.json")],
    [new FModelTextureLinkProjection(0, "BaseColor", "/Game/T_BC.T_BC", "../Texture/T_BC.png", "tex.relative", PbrMapKind.BaseColor, true, "Body")],
    Array.Empty<FModelUeFormatPipelineDiagnostic>(),
    [new FModelTextureResourceProjection("tex.relative", "/Game/T_BC.T_BC", "../Texture/T_BC.png", 4, Enumerable.Repeat((byte)0x44, 32).ToArray(), LocationMode: FModelTextureResourceLocation.Relative)]);

var duplicateTextureProjection = new FModelMaterialProjection(
    "/Game/SM_Test.SM_Test",
    projection.Materials,
    [new FModelTextureLinkProjection(0, "BaseColor", "/Game/T_BC.T_BC", "textures/T_BC.png", "tex.base", PbrMapKind.BaseColor, true, "Body"),
     new FModelTextureLinkProjection(0, "Diffuse", "/Game/T_BC.T_BC", "textures/T_BC.png", "tex.base", PbrMapKind.BaseColor, true, "Body")],
    Array.Empty<FModelUeFormatPipelineDiagnostic>(),
    textureProjection.TextureResources);

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

var root = new FDataAttributeSet();
root.AddAttribute("LODS", attribute => attribute.Write(0));
var v10Writer = new FModelV10AttributeWriter();
v10Writer.WriteProjection(root, relativeProjection, 1);
using var attributesArchive = new FArchiveWriter();
root.Serialize(attributesArchive);
var v10Attributes = ReadV10Attributes(attributesArchive.GetBuffer());
var materialAttribute = v10Attributes["FMODEL_MATERIALS"];
var textureAttribute = v10Attributes["FMODEL_TEXTURES"];
var inspectedTexture = FModelTextureChunkInspector.Inspect(textureAttribute.Payload);
if (materialAttribute.Count != 1 || materialAttribute.Payload[0] != 2
    || inspectedTexture.Version != 2
    || inspectedTexture.Textures.Single().LogicalResourceUri != "../Texture/T_BC.png"
    || inspectedTexture.Textures.Single().LocationMode != FModelTextureResourceLocation.Relative
    || inspectedTexture.Bindings.Single().MapKind != PbrMapKind.BaseColor)
    return 1;

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



Console.WriteLine($"v10MaterialVersion={materialAttribute.Payload[0]}; v10TextureVersion={inspectedTexture.Version}; v10RelativeUri={inspectedTexture.Textures.Single().LogicalResourceUri}; linkedComplete={linkedProjection.IsComplete}; strictErrors={strictErrors}");
Console.WriteLine($"duplicateResourcesComplete={duplicateResourceProjection.IsComplete}; mipResource={mipProjection.Textures.Single().TextureResourceId}");
Console.WriteLine($"queueReaddResults={queueResults.Count}; queueReaddByte={queueByte}; queueTotal={queueSession.TotalQueued}");
Console.WriteLine($"queueSameInstanceResults={sameInstanceResults.Count}; queueSameInstanceByte={sameInstanceByte}; queueSameInstanceTotal={sameInstanceSession.TotalQueued}");
Console.WriteLine("queueRemovedExporterCollected=True");
if (!linkedProjection.IsComplete || strictErrors == 0)
    return 1;
if (!duplicateResourceProjection.IsComplete || duplicateResourceProjection.TextureResources.Count != 1)
    return 1;
if (mipProjection.Textures.Single().TextureResourceId != "tex.mip2")
    return 1;
if (queueResults.Count != 1 || !queueResults[0].Success || queueByte != 0x22 || queueSession.TotalQueued != 0)
    return 1;
if (sameInstanceResults.Count != 1 || !sameInstanceResults[0].Success || sameInstanceByte != 0x33 || sameInstanceSession.TotalQueued != 0)
    return 1;
var removedSession = new ExportSession();
var removedWeakReference = RemovedQueueProbe.AddAndRemove(removedSession);
for (var collection = 0; collection < 3; collection++)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}
if (removedWeakReference.TryGetTarget(out _))
    return 1;
removedSession.Dispose();

var cacheOutputRoot = Path.Combine(Path.GetTempPath(), "fmodel-export-session-cache-release-" + Guid.NewGuid().ToString("N"));
var cacheSession = new ExportSession
{
    Extension = new NoopExportSessionExtension(),
    MaxDegreeOfParallelism = 1
};
var cacheExporter = new QueueHarnessExporter("Game/Queue/Cache.asset", 0x44);
var cacheProgress = new CacheReleaseProgress(cacheExporter);
cacheSession.Add(cacheExporter);
try
{
    var cacheResults = await cacheSession.RunAsync(cacheOutputRoot, new ExportOptions(), cacheProgress);
    if (cacheResults.Count != 1 || !cacheResults[0].Success || !cacheProgress.CacheReleased)
        return 1;
}
finally
{
    cacheSession.Dispose();
    if (Directory.Exists(cacheOutputRoot))
        Directory.Delete(cacheOutputRoot, recursive: true);
}

var lifetimeOutputRoot = Path.Combine(Path.GetTempPath(), "fmodel-export-session-lifetime-" + Guid.NewGuid().ToString("N"));
var lifetimeSession = new ExportSession { MaxDegreeOfParallelism = 1 };
QueueHarnessExporter lifetimeRoot = null!;
QueueProbeExporter? lifetimeProbe = null;
lifetimeRoot = new QueueHarnessExporter(
    "Game/Queue/Lifetime.asset",
    0x55,
    session =>
    {
        lifetimeProbe ??= new QueueProbeExporter(lifetimeRoot.ObjectPath);
        session.Add(lifetimeProbe);
    });
lifetimeSession.Add(lifetimeRoot);
try
{
    var lifetimeResults = await lifetimeSession.RunAsync(lifetimeOutputRoot, new ExportOptions());
    if (lifetimeResults.Count != 2 || lifetimeProbe is null || !lifetimeProbe.StrongExporterReleased)
        return 1;
}
finally
{
    lifetimeSession.Dispose();
    if (Directory.Exists(lifetimeOutputRoot))
        Directory.Delete(lifetimeOutputRoot, recursive: true);
}

var priorityOutputRoot = Path.Combine(Path.GetTempPath(), "fmodel-export-session-priority-" + Guid.NewGuid().ToString("N"));
var prioritySession = new ExportSession { MaxDegreeOfParallelism = 1 };
var schedulingOrder = new List<string>();
prioritySession.Add(new OrderedQueueExporter("Game/Queue/RootA.asset", "RootA", schedulingOrder, session =>
    session.Add(new OrderedQueueExporter("Game/Queue/Dependency.asset", "Dependency", schedulingOrder))));
prioritySession.Add(new OrderedQueueExporter("Game/Queue/RootB.asset", "RootB", schedulingOrder));
try
{
    var priorityResults = await prioritySession.RunAsync(priorityOutputRoot, new ExportOptions());
    if (priorityResults.Count != 3 || !schedulingOrder.SequenceEqual(["RootA", "Dependency", "RootB"]))
        return 1;
}
finally
{
    prioritySession.Dispose();
    if (Directory.Exists(priorityOutputRoot))
        Directory.Delete(priorityOutputRoot, recursive: true);
}

var producerOutputRoot = Path.Combine(Path.GetTempPath(), "fmodel-export-session-producer-" + Guid.NewGuid().ToString("N"));
var producerSession = new ExportSession { MaxDegreeOfParallelism = 2 };
var producerLease = producerSession.OpenProducer();
try
{
    var producerRun = producerSession.RunAsync(producerOutputRoot, new ExportOptions());
    await Task.Delay(50);
    if (producerRun.IsCompleted)
        return 1;

    producerSession.Add(new QueueHarnessExporter("Game/Queue/ProducedA.asset", 0x71));
    producerSession.Add(new QueueHarnessExporter("Game/Queue/ProducedB.asset", 0x72));
    producerLease.Dispose();
    var producerResults = await producerRun;
    if (producerResults.Count != 2 || producerSession.TotalQueued != 0 || producerSession.IsRunning)
        return 1;
}
finally
{
    producerLease.Dispose();
    producerSession.Dispose();
    if (Directory.Exists(producerOutputRoot))
        Directory.Delete(producerOutputRoot, recursive: true);
}

var stressOutputRoot = Path.Combine(Path.GetTempPath(), "fmodel-export-session-stress-" + Guid.NewGuid().ToString("N"));
var stressState = new ConcurrencyProbeState();
using (var stressSession = new ExportSession { MaxDegreeOfParallelism = 4 })
{
    for (var index = 0; index < 2048; index++)
        stressSession.Add(new ConcurrencyProbeExporter($"Game/Queue/Stress{index}.asset", stressState));

    try
    {
        var stressResults = await stressSession.RunAsync(stressOutputRoot, new ExportOptions());
        if (stressResults.Count != 2048 || stressState.Peak is < 2 or > 4 || stressSession.TotalQueued != 0)
            return 1;
    }
    finally
    {
        if (Directory.Exists(stressOutputRoot))
            Directory.Delete(stressOutputRoot, recursive: true);
    }
}

using (var cancellationSession = new ExportSession { MaxDegreeOfParallelism = 2 })
using (var cancellationSource = new CancellationTokenSource())
{
    var cancellationProducer = cancellationSession.OpenProducer();
    try
    {
        var canceledRun = cancellationSession.RunAsync(
            Path.Combine(Path.GetTempPath(), "fmodel-export-session-canceled-" + Guid.NewGuid().ToString("N")),
            new ExportOptions(),
            ct: cancellationSource.Token);
        await Task.Delay(50);
        await cancellationSource.CancelAsync();
        try
        {
            await canceledRun;
            return 1;
        }
        catch (OperationCanceledException)
        {
            if (cancellationSession.IsRunning || cancellationSession.TotalQueued != 0)
                return 1;
        }
    }
    finally
    {
        cancellationProducer.Dispose();
    }
}

var callbackSession = new ExportSession { MaxDegreeOfParallelism = 1 };
var callbackOutputRoot = Path.Combine(Path.GetTempPath(), "fmodel-export-session-callback-" + Guid.NewGuid().ToString("N"));
callbackSession.Add(new QueueHarnessExporter("Game/Queue/Callback.asset", 0x66));
try
{
    var callbackResults = await callbackSession.RunAsync(callbackOutputRoot, new ExportOptions(), new ThrowingProgress());
    if (callbackResults.Count != 1 || callbackSession.IsRunning || callbackSession.TotalQueued != 0)
        return 1;
}
finally
{
    callbackSession.Dispose();
    if (Directory.Exists(callbackOutputRoot))
        Directory.Delete(callbackOutputRoot, recursive: true);
}

static Dictionary<string, (int Count, byte[] Payload)> ReadV10Attributes(byte[] bytes)
{
    using var stream = new MemoryStream(bytes, writable: false);
    using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: false);
    var count = reader.ReadInt32();
    if (count < 0 || count > 4096)
        throw new InvalidDataException("Invalid V10 attribute count.");
    var result = new Dictionary<string, (int Count, byte[] Payload)>(StringComparer.Ordinal);
    for (var index = 0; index < count; index++)
    {
        var name = ReadFString(reader);
        var length = reader.ReadInt32();
        if (length < 5 || length > stream.Length - stream.Position)
            throw new InvalidDataException("Invalid V10 attribute length.");
        var envelopeVersion = reader.ReadByte();
        var arraySize = reader.ReadInt32();
        if (envelopeVersion != 1 || arraySize < 0 || !result.TryAdd(name, (arraySize, reader.ReadBytes(length - 5))))
            throw new InvalidDataException("Invalid or duplicate V10 FModel attribute.");
    }
    if (stream.Position != stream.Length)
        throw new InvalidDataException("Trailing V10 attribute data.");
    return result;
}

static string ReadFString(BinaryReader reader)
{
    var length = reader.ReadInt32();
    if (length <= 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
        throw new InvalidDataException("Invalid FString length.");
    var data = reader.ReadBytes(length);
    return System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0');
}
return 0;

sealed class QueueHarnessExporter : ExporterBase
{
    private readonly byte value;
    private readonly Action<ExportSession>? onBuild;

    public QueueHarnessExporter(string path, byte value, Action<ExportSession>? onBuild = null)
        : base(new QueueHarnessGameFile(path), "QueueHarness")
    {
        this.value = value;
        this.onBuild = onBuild;
    }

    protected override IReadOnlyList<ExportFile> BuildExportFiles(CancellationToken ct = default)
    {
        onBuild?.Invoke(Session);
        return [new ExportFile("bin", [value])];
    }
}

static class RemovedQueueProbe
{
    public static WeakReference<ExporterBase> AddAndRemove(ExportSession session)
    {
        var exporter = new QueueHarnessExporter("Game/Queue/Removed.asset", 0x12);
        session.Add(exporter);
        if (!session.Remove(exporter.ObjectPath))
            throw new InvalidOperationException("Synthetic removed exporter was not removed from the queue.");
        return new WeakReference<ExporterBase>(exporter);
    }
}

sealed class QueueProbeExporter : ExporterBase
{
    public QueueProbeExporter(string targetPath)
        : base(new QueueHarnessGameFile("Game/Queue/LifetimeProbe.asset"), "QueueProbe") => TargetPath = targetPath;

    public string TargetPath { get; }
    public bool StrongExporterReleased { get; private set; }

    protected override IReadOnlyList<ExportFile> BuildExportFiles(CancellationToken ct = default)
    {
        var taskQueue = typeof(ExportSession)
            .GetField("_taskQueue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(Session)!;
        var registrations = taskQueue.GetType()
            .GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(taskQueue)!;
        var registration = registrations.GetType()
            .GetProperty("Item")!
            .GetValue(registrations, [TargetPath])!;
        var strong = registration.GetType()
            .GetField("_strongExporter", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(registration);
        StrongExporterReleased = strong is null;
        return [new ExportFile("bin", [0x77])];
    }
}

sealed class OrderedQueueExporter : ExporterBase
{
    private readonly string name;
    private readonly List<string> order;
    private readonly Action<ExportSession>? enqueue;

    public OrderedQueueExporter(string path, string name, List<string> order, Action<ExportSession>? enqueue = null)
        : base(new QueueHarnessGameFile(path), "OrderedQueue")
    {
        this.name = name;
        this.order = order;
        this.enqueue = enqueue;
    }

    protected override IReadOnlyList<ExportFile> BuildExportFiles(CancellationToken ct = default)
    {
        order.Add(name);
        enqueue?.Invoke(Session);
        return [new ExportFile("bin", [0x7A])];
    }
}

sealed class ConcurrencyProbeState
{
    private int active;
    private int peak;

    public int Peak => Volatile.Read(ref peak);

    public void Enter()
    {
        var current = Interlocked.Increment(ref active);
        while (true)
        {
            var observed = Volatile.Read(ref peak);
            if (current <= observed || Interlocked.CompareExchange(ref peak, current, observed) == observed)
                return;
        }
    }

    public void Exit() => Interlocked.Decrement(ref active);
}

sealed class ConcurrencyProbeExporter : ExporterBase
{
    private readonly ConcurrencyProbeState state;

    public ConcurrencyProbeExporter(string path, ConcurrencyProbeState state)
        : base(new QueueHarnessGameFile(path), "ConcurrencyProbe") => this.state = state;

    public override async Task<ExportResult> ExportAsync(CancellationToken ct = default)
    {
        state.Enter();
        try
        {
            await Task.Delay(1, ct);
            return new ExportResult(true, ObjectPath, []);
        }
        finally
        {
            state.Exit();
        }
    }

    protected override IReadOnlyList<ExportFile> BuildExportFiles(CancellationToken ct = default) =>
        throw new NotSupportedException();
}

sealed class CacheReleaseProgress(ExporterBase exporter) : IProgress<ExportProgress>
{
    public bool CacheReleased { get; private set; }

    public void Report(ExportProgress value)
    {
        var cached = typeof(ExporterBase)
            .GetField("_builtExportFiles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(exporter);
        CacheReleased = cached is null;
    }
}

sealed class ThrowingProgress : IProgress<ExportProgress>
{
    public void Report(ExportProgress value) => throw new InvalidOperationException("synthetic progress failure");
}

sealed class NoopExportSessionExtension : IExportSessionExtension
{
    public IMeshExportFormat? TryCreateMeshFormat(UObject export, EMeshFormat format, ExportSession session) => null;
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

