using System.Reflection;
using System.Security.Cryptography;
using CUE4Parse.FModelUEFormat.ExportPipeline.Delivery;
using CUE4Parse.FModelUEFormat.ExportPipeline.Diagnostics;
using CUE4Parse.FModelUEFormat.ExportPipeline.Formats;
using CUE4Parse.FModelUEFormat.ExportPipeline.Materials;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.UeFormat.UEModel;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Writers.UEFormat.Structs;
using FModel.Services;

// Synthetic bytes and actual filesystem/result verifier. No game asset/native
// texture encoder/Blender GUI. Upstream root write results remain unchanged;
// the host consumes scoped, effective terminal results for declared resources.
internal static class TerminalDeliveryChecks
{
    internal static void Run()
    {
        int count = 0;
        foreach (var scenario in new[] { "success", "failed_dependency", "stale_file", "missing_file", "bad_length", "bad_hash", "escape", "backslash", "unsupported_mode", "export_root", "unrelated_failure", "root_already_failed", "shared", "shared_conflict", "canceled" })
        {
            var temp = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "fmodel-terminal-delivery-" + Guid.NewGuid().ToString("N")));
            try
            {
                var model = Path.Combine(temp, "CodeWorld", "Content", "Models", "M.uemodel");
                var texture = Path.Combine(temp, "CodeWorld", "Content", "Textures", "T_贴图_MIP2.png");
                Directory.CreateDirectory(Path.GetDirectoryName(model)!);
                Directory.CreateDirectory(Path.GetDirectoryName(texture)!);
                File.WriteAllBytes(model, [0x55]);
                var bytes = new byte[] { 1, 2, 3, 4 };
                if (scenario is not ("failed_dependency" or "missing_file")) File.WriteAllBytes(texture, bytes);
                if (scenario == "bad_length") File.WriteAllBytes(texture, [1, 2]);
                if (scenario == "bad_hash") File.WriteAllBytes(texture, [4, 3, 2, 1]);
                var rootId = "CodeWorld/Content/Models/M.M";
                var resource = new FModelTextureResourceProjection("texture", "/Game/T.T", "../Textures/T_贴图_MIP2.png", 4, SHA256.HashData(bytes), LocationMode: FModelTextureResourceLocation.Relative);
                if (scenario == "escape") resource = resource with { LogicalResourceUri = "../../../../outside.png" };
                if (scenario == "backslash") resource = resource with { LogicalResourceUri = "..\\Textures\\T.png" };
                if (scenario == "unsupported_mode") resource = resource with { LocationMode = FModelTextureResourceLocation.Bundle };
                if (scenario == "export_root") resource = resource with { LogicalResourceUri = "CodeWorld/Content/Textures/T_贴图_MIP2.png", LocationMode = FModelTextureResourceLocation.ExportRoot };
                var declarations = new List<FModelUeFormatDeliveryDeclaration> { new(rootId, "CodeWorld/Content/Models/M.uemodel", [resource]) };
                var originalError = new IOException("Original root failure");
                var results = new List<ExportResult> {
                    scenario == "root_already_failed" ? ExportResult.Failure(rootId, originalError) : new(true, rootId, [model]),
                    scenario is "failed_dependency" or "stale_file"
                        ? ExportResult.Failure("CodeWorld/Content/Textures/T.T", new IOException("Dependency write failed"))
                        : new(true, "CodeWorld/Content/Textures/T.T", [texture])
                };
                if (scenario == "unrelated_failure") results.Add(ExportResult.Failure("UnrelatedOfficial.MaterialJson", new IOException("Optional unrelated result")));
                if (scenario is "shared" or "shared_conflict")
                {
                    var model2 = Path.Combine(temp, "CodeWorld", "Content", "Models", "M2.uemodel");
                    File.WriteAllBytes(model2, [0x55]);
                    var root2 = "CodeWorld/Content/Models/M2.M2";
                    declarations.Add(new(root2, "CodeWorld/Content/Models/M2.uemodel", [scenario == "shared_conflict" ? resource with { Sha256 = SHA256.HashData([9, 9, 9, 9]) } : resource]));
                    results.Add(new(true, root2, [model2]));
                }
                if (scenario == "canceled")
                {
                    try { FModelUeFormatDeliveryVerifier.Validate(results, temp, declarations, new CancellationToken(true)); }
                    catch (OperationCanceledException) { count++; continue; }
                    throw new Exception("Canceled terminal validation did not propagate cancellation.");
                }
                var validated = FModelUeFormatDeliveryVerifier.Validate(results, temp, declarations);
                var succeeds = scenario is "success" or "export_root" or "unrelated_failure" or "shared";
                if (validated.Results[0].Success != succeeds) throw new Exception($"Unexpected terminal result: {scenario}");
                if (scenario == "root_already_failed" && !ReferenceEquals(validated.Results[0].Error, originalError)) throw new Exception("Root error was replaced.");
                if (succeeds && (!ReferenceEquals(validated.Results, results) || validated.VerifiedTextureFileCount != 1)) throw new Exception("Success copies results or repeats shared hashing.");
                if (!succeeds && scenario != "root_already_failed" && (!results[0].Success || validated.Results[0].DiskFilePaths![0] != model)) throw new Exception("Upstream result was mutated or written model paths lost.");
                if (scenario == "shared_conflict" && validated.Results[2].Success) throw new Exception("Conflicting shared model was accepted.");
                var untouched = FModelUeFormatDeliveryVerifier.Validate(results, temp, Array.Empty<FModelUeFormatDeliveryDeclaration>());
                if (!ReferenceEquals(untouched.Results, results)) throw new Exception("Standard-only/official results changed.");
                count++;
            }
            finally
            {
                var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!temp.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(temp).StartsWith("fmodel-terminal-delivery-", StringComparison.Ordinal)) throw new Exception("Unsafe test cleanup.");
                if (Directory.Exists(temp))
                {
                    if ((File.GetAttributes(temp) & FileAttributes.ReparsePoint) != 0) throw new Exception("Test root is a reparse point.");
                    Directory.Delete(temp, true);
                }
            }
        }
        CheckPublicationAndRegistry();
        Console.WriteLine($"TerminalDeliveryChecks: {count} filesystem/result cases + publication/registry PASS (required failure, current-write evidence, SHA, URI, shared dedup, cancellation, unrelated/standard-only isolation).");
    }

    private static void CheckPublicationAndRegistry()
    {
        var operation = "publication-" + Guid.NewGuid().ToString("N");
        var projection = new FModelMaterialProjection(
            "/Game/M.M",
            [new FModelMaterialLinkProjection(0, 0, "Body", "CodeWorld/Content/Material/M_Body.M_Body", "../Material/M_Body.json")],
            [new FModelTextureLinkProjection(0, "BaseColor", "/Game/T.T", "../Textures/T.png", "texture", PbrMapKind.BaseColor, true, "Body")],
            Array.Empty<FModelUeFormatPipelineDiagnostic>(),
            [new FModelTextureResourceProjection("texture", "/Game/T.T", "../Textures/T.png", 4, SHA256.HashData([1, 2, 3, 4]), LocationMode: FModelTextureResourceLocation.Relative)]);
        using var session = new ExportSession();
        var format = new FModelUeFormatMeshFormat(new FModelUeFormatPolicy(true, FModelUeFormatMaterialMode.Linked), session,
            new UStaticMesh { Name = "M" }, deliverySink: declaration => FModelV3ExportDelivery.Record(operation, declaration));
        var build = typeof(FModelUeFormatMeshFormat).GetMethod("BuildExtension", BindingFlags.NonPublic | BindingFlags.Instance)!;
        void Publish(FModelMaterialProjection value)
        {
            var callback = (Action<FDataAttributeSet>)build.Invoke(format, ["/Game/M.M", value, 1, null, "CodeWorld/Content/Models/M.M", "CodeWorld/Content/Models/M.uemodel"])!;
            callback(new FDataAttributeSet());
        }
        Publish(projection);
        var records = FModelV3ExportDelivery.Take(operation);
        if (records.Count != 1 || records[0].RootObjectPath != "CodeWorld/Content/Models/M.M" || records[0].Resources.Count != 1 || FModelV3ExportDelivery.Take(operation).Count != 0)
            throw new Exception("Publication records do not use exporter result identity or are retained after consumption.");
        Publish(projection with { Textures = Array.Empty<FModelTextureLinkProjection>() });
        if (FModelV3ExportDelivery.Take(operation).Count != 0) throw new Exception("Unserialized texture resources were registered.");
        Publish(projection with { Materials = [new FModelMaterialLinkProjection(0, 0, "Body", "bad", "C:/unsafe.json")] });
        if (FModelV3ExportDelivery.Take(operation).Count != 0) throw new Exception("Linked fallback retained a delivery requirement.");
        Publish(projection);
        FModelV3ExportDelivery.Clear(operation);
        if (FModelV3ExportDelivery.Take(operation).Count != 0) throw new Exception("Abandoned/canceled delivery registry was not cleared.");
    }
}
