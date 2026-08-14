using System.IO;
using FModel.Framework;

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

Check(FModelV3ContainerFormats.ClassifyPath("pakchunk0-Windows.pak") == FModelV3ContainerFormat.Pak,
    "PAK must classify as Pak.");
Check(FModelV3ContainerFormats.ClassifyPath("pakchunk0-Windows.upak") == FModelV3ContainerFormat.Pak,
    "UPAK must classify as Pak-compatible.");
Check(FModelV3ContainerFormats.ClassifyPath("global.utoc") == FModelV3ContainerFormat.IoStore,
    "UTOC must classify as IoStore.");
Check(FModelV3ContainerFormats.ClassifyPath("pakchunk0.uondemandtoc") == FModelV3ContainerFormat.IoStoreOnDemandDescriptor,
    "OnDemand TOC must remain a descriptor, not a selectable mounted reader.");
Check(FModelV3ContainerFormats.ClassifyPath("pakchunk0.ucas") == FModelV3ContainerFormat.IoStoreDataSidecar,
    "UCAS must remain an IoStore sidecar.");
Check(FModelV3ContainerFormats.ClassifyPath("pakchunk0.sig") == FModelV3ContainerFormat.SignatureSidecar,
    "SIG must remain a signature sidecar.");
Check(FModelV3ContainerFormats.IsSelectablePath("pakchunk0-Windows.pak"),
    "PAK must be selectable.");
Check(FModelV3ContainerFormats.IsSelectablePath("pakchunk0-Windows.upak"),
    "UPAK must be selectable when the provider registered a reader.");
Check(!FModelV3ContainerFormats.IsSelectablePath("pakchunk0.ucas"),
    "UCAS must not be independently selectable.");
Check(FModelV3ContainerFormats.IsDescriptorPath("pakchunk0.uondemandtoc"),
    "OnDemand TOC must be recognized as a descriptor.");
Check(FModelV3ContainerFormats.IsSidecarPath("pakchunk0.sig"),
    "SIG must be recognized as a sidecar.");

Check(FModelV3ContainerIdentity.GetContainerDirectoryName("pakchunk0-Windows.upak") == "pakchunk0-Windows",
    "UPAK suffix must be removed from the output directory name.");

var root = Path.Combine(Path.GetTempPath(), "fmodel-container-support-harness");
var safePath = FModelV3ContainerIdentity.CombineContainedPath(root, "/Game/Content/Example.uasset");
Check(safePath.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
    "Virtual path must stay below the export root.");

var rejected = 0;
foreach (var unsafePath in new[] { "../escape.uasset", "/../../escape.uasset", "C:/escape.uasset", "Game/../escape.uasset", "Game/:bad.uasset", "//server/share.uasset" })
{
    try
    {
        FModelV3ContainerIdentity.CombineContainedPath(root, unsafePath);
    }
    catch (ArgumentException)
    {
        rejected++;
    }
}
Check(rejected == 6, "All unsafe virtual paths must be rejected.");

Console.WriteLine("FModel container support harness: PASS");
