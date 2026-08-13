#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace FModel.Services;

internal static class FModelV3NewPipelineOneShotExecution
{
    internal static bool IsExactExportIdentity(object loadedObject, object? resolvedExport)
        => ReferenceEquals(loadedObject, resolvedExport);

    internal static void RequireExactExportIdentity(object loadedObject, object? resolvedExport)
    {
        if (!IsExactExportIdentity(loadedObject, resolvedExport))
            throw new InvalidOperationException("The resolved owner export does not match the loaded static mesh identity.");
    }

    internal static async Task RunAsync(Func<CancellationToken, Task> action, Action<int> shutdown)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(shutdown);

        var exitCode = 1;
        var result = "failed";
        try
        {
            await Task.Run(() => action(CancellationToken.None));
            exitCode = 0;
            result = "succeeded";
        }
        catch (OperationCanceledException)
        {
            result = "cancelled";
        }
        catch (Exception exception)
        {
            Log.Error(exception, "v3.newpipeline.oneshot failed");
        }
        finally
        {
            Log.Information("v3.newpipeline.oneshot terminal result {Result} exitCode {ExitCode}", result, exitCode);
            shutdown(exitCode);
        }
    }
}
