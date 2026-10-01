using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Compile the ACTUAL host progress methods, with a deterministic dispatcher and
// scalar properties replacing WPF/config initialization. No WPF window, settings,
// package UObject, native image decoder or Blender is loaded by this harness.
var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root is not null && !File.Exists(Path.Combine(root.FullName, "FModel/ViewModels/ExportSessionViewModel.cs"))) root = root.Parent;
if (root is null) throw new Exception("Host source root not found.");
var host = File.ReadAllText(Path.Combine(root.FullName, "FModel/ViewModels/ExportSessionViewModel.cs"));
var syntax = CSharpSyntaxTree.ParseText(host).GetRoot();
var names = new[] { "BeginProgressGeneration", "IsCurrentProgressGeneration", "QueueProgress", "ScheduleProgressDrain", "DrainProgress", "SetUeFormatTerminalProgress" };
var methods = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(method => names.Contains(method.Identifier.Text)).ToArray();
if (methods.Count(method => method.Identifier.Text != "SetUeFormatTerminalProgress") != 5) throw new Exception("Host method extraction drifted.");
var terminal = methods.Any(method => method.Identifier.Text == "SetUeFormatTerminalProgress");
if (!terminal && !(host.Contains("SucceededCount = results.Count(static result => result.Success)") && host.Contains("FailedCount = results.Count - SucceededCount"))) throw new Exception("Legacy terminal reproduction does not match host source.");
var final = terminal ? "vm.SetUeFormatTerminalProgress(results, generation);" : "vm.CompletedCount = results.Count; vm.SucceededCount = results.Count(static result => result.Success); vm.FailedCount = results.Count - vm.SucceededCount;";
var wrapper = """
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CUE4Parse_Conversion;
namespace CUE4Parse_Conversion {
    public record ExportResult(bool Success, string ObjectPath);
    public readonly record struct ExportProgress(int Completed, int Total, ExportResult? LastResult = null);
}
public enum DispatcherPriority { Background }
public sealed class FakeDispatcher {
    private readonly Queue<Action> pending = new();
    public bool CheckAccess() => false;
    public void InvokeAsync(Action action, DispatcherPriority priority) => pending.Enqueue(action);
    public void Flush() { while (pending.TryDequeue(out var action)) action(); }
}
public sealed class Application {
    public static Application Current { get; } = new();
    public FakeDispatcher Dispatcher { get; } = new();
}
public class HostProgress {
    private readonly object _progressLock = new();
    private long _progressGeneration;
    private int _progressDrainScheduled, _progressSucceededCount, _progressFailedCount;
    private ExportProgress _latestProgress;
    public int CompletedCount, SucceededCount, FailedCount;
    public string? CurrentItemName;
    public double ProgressValue;
    public static void Run() {
        var vm = new HostProgress();
        var model = new ExportResult(true, "CodeWorld/Content/M.M");
        var failed = new ExportResult(false, "CodeWorld/Content/T.T");
        var generation = vm.BeginProgressGeneration();
        vm.QueueProgress(new ExportProgress(1, 2, model), generation);
        vm.QueueProgress(new ExportProgress(2, 2, failed), generation);
        void Final(IReadOnlyList<ExportResult> results, long gen) { Set(vm, results, gen); }
        Final(new[] { model with { Success = false }, failed }, generation);
        Application.Current.Dispatcher.Flush(); // older Background update after terminal state
        if (vm.SucceededCount != 0 || vm.FailedCount != 2 || vm.CompletedCount != 2)
            throw new Exception($"Terminal progress overwritten: succeeded={vm.SucceededCount}, failed={vm.FailedCount}, completed={vm.CompletedCount}");
        vm.DrainProgress();
        if (vm.FailedCount != 2) throw new Exception("Repeated drain loses terminal failure.");
        var next = vm.BeginProgressGeneration();
        vm.QueueProgress(new ExportProgress(1, 1, model), next);
        Final(new[] { failed }, generation);
        Application.Current.Dispatcher.Flush();
        if (vm.SucceededCount != 1 || vm.FailedCount != 0) throw new Exception("Stale terminal crossed into next generation.");
        Final(new[] { model, model }, next);
        Application.Current.Dispatcher.Flush();
        if (vm.SucceededCount != 2 || vm.FailedCount != 0) throw new Exception("Success terminal drifted.");
        Final(Array.Empty<ExportResult>(), next);
        Application.Current.Dispatcher.Flush();
        if (vm.CompletedCount != 0 || vm.FailedCount != 0 || vm.ProgressValue != 0) throw new Exception("Empty terminal drifted.");
    }
    private static void Set(HostProgress vm, IReadOnlyList<ExportResult> results, long generation) { REPLACE_FINAL }
    REPLACE_METHODS
}
""".Replace("REPLACE_FINAL", final).Replace("REPLACE_METHODS", string.Join("\n", methods.Select(method => method.ToFullString())));
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("HostProgressProbe", [CSharpSyntaxTree.ParseText(wrapper)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
using var output = new MemoryStream();
var emitted = compilation.Emit(output);
if (!emitted.Success) throw new Exception(string.Join("\n", emitted.Diagnostics));
var assembly = Assembly.Load(output.ToArray());
try {
    assembly.GetType("HostProgress")!.GetMethod("Run")!.Invoke(null, null);
    Console.WriteLine("TerminalProgressChecks: 5 cases PASS; actual host methods + deterministic Background dispatcher (no WPF/GUI/settings/native decoder).");
    return 0;
} catch (TargetInvocationException error) {
    Console.WriteLine("FINDING: " + error.InnerException!.Message);
    return 2;
}
