using System.Security.Cryptography;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Exporters;
using CUE4Parse_Conversion.Options;
using CUE4Parse.FModelUEFormat.ExportPipeline.Materials;

// Synthetic queue/persistence boundary check, not real UTexture encoding, native
// decoding or shader parity. It explicitly distinguishes root success from
// complete resource delivery: Strict projection is not a filesystem transaction.
internal static class TextureDeliveryBoundaryChecks
{
    internal static async Task RunAsync()
    {
        foreach (var failWrite in new[] { false, true })
        {
            var outputRoot = Path.Combine(Path.GetTempPath(), "fmodel-delivery-boundary-" + Guid.NewGuid().ToString("N"));
            using var session = new ExportSession
            {
                Extension = new NoopExportSessionExtension(),
                MaxDegreeOfParallelism = 1
            };
            var dependency = new SyntheticTexture(failWrite);
            var model = new SyntheticModel(dependency);
            session.Add(model);
            try
            {
                var results = await session.RunAsync(outputRoot, new ExportOptions(meshFormat: EMeshFormat.UEFormat));
                var modelResult = results.Single(result => result.ObjectPath == model.ObjectPath);
                var dependencyResult = results.Single(result => result.ObjectPath == dependency.ObjectPath);
                if (!modelResult.Success || dependencyResult.Success == failWrite)
                    throw new InvalidOperationException("Unexpected root/dependency delivery status.");
                var modelPath = modelResult.DiskFilePaths!.Single();
                var texturePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(modelPath)!, model.RelativeUri!));
                if (!File.Exists(modelPath))
                    throw new InvalidOperationException("Successful root was not written.");
                if (!failWrite)
                {
                    var bytes = File.ReadAllBytes(texturePath);
                    if (texturePath != dependencyResult.DiskFilePaths!.Single()
                        || bytes.LongLength != model.ProjectedLength
                        || !SHA256.HashData(bytes).SequenceEqual(model.ProjectedHash!))
                        throw new InvalidOperationException("Projected URI/bytes differ from actual upstream persistence.");
                }
                else if (File.Exists(texturePath))
                    throw new InvalidOperationException("Deliberately failed dependency unexpectedly delivered a file.");
            }
            finally
            {
                // Exact unique test-owned absolute directory only; reject reparse
                // roots before removing the synthetic output (no user exports).
                var full = Path.GetFullPath(outputRoot);
                var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(full).StartsWith("fmodel-delivery-boundary-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unsafe synthetic output cleanup.");
                if (Directory.Exists(full))
                {
                    if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("Synthetic output root is a reparse point.");
                    Directory.Delete(full, recursive: true);
                }
            }
        }
        Console.WriteLine("TextureDeliveryBoundaryChecks: 2 cases PASS; URI/size/SHA match on success; root success can coexist with dependency write failure.");
    }

    private sealed class SyntheticTexture(bool failWrite) : ExporterBase(new QueueHarnessGameFile("CodeWorld/Content/Textures/T_Main.asset"), "SyntheticTexture")
    {
        protected override IReadOnlyList<ExportFile> BuildExportFiles(CancellationToken ct = default) =>
            [new ExportFile("png", [0x01, 0x02, 0x03, 0x04], "_MIP2")];

        protected override (string, string) ResolveOutputPath(ExportFile file)
        {
            if (failWrite) throw new IOException("Synthetic dependency write failure after successful projection.");
            return base.ResolveOutputPath(file);
        }
    }

    private sealed class SyntheticModel(SyntheticTexture dependency) : ExporterBase(new QueueHarnessGameFile("CodeWorld/Content/Models/Model.asset"), "SyntheticModel")
    {
        public string? RelativeUri { get; private set; }
        public long ProjectedLength { get; private set; }
        public byte[]? ProjectedHash { get; private set; }

        protected override IReadOnlyList<ExportFile> BuildExportFiles(CancellationToken ct = default)
        {
            var canonical = Session.AddOrGet(dependency);
            var file = canonical.BuildExportFilesForProjection(ct).Single();
            var targetUri = dependency.SavePath + file.NameSuffix + "." + file.Extension;
            RelativeUri = FModelMaterialUriResolver.ResolveRelativeUri(SavePath + ".uemodel", targetUri);
            ProjectedLength = file.Data.LongLength;
            ProjectedHash = SHA256.HashData(file.Data);
            // The bytes are deliberately not a real UEMODEL fixture. Only the
            // upstream Session/Exporter persistence and ordering are exercised.
            return [new ExportFile("uemodel", [0x55])];
        }
    }
}
