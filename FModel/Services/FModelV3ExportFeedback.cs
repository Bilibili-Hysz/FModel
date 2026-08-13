#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CUE4Parse_Conversion;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using FModel.Framework;
using FModel.ViewModels;
using FModel.Views.Resources.Controls;

namespace FModel.Services;

/// <summary>
/// Mirrors the terminal state of V3-owned ExportSession work into FModel's
/// bottom Print area. Detailed exporter events remain in Export Session.
/// </summary>
internal static class FModelV3ExportFeedback
{

    internal static void ReportMaterialModeChanged(FModelUeFormatMaterialMode mode)
    {
        if (FLogger.Logger is null)
            return;

        FLogger.Append(ELog.Information, () =>
            FLogger.Text(
                $"UEFormat material-link policy set to {mode}; it applies when the next UEFormat mesh is serialized. Non-UEFormat exports are unaffected.",
                FModel.Constants.WHITE,
                true));
    }

    internal static void ReportContainerStarted(string containerName, string destinationDirectory, int packageCount)
    {
        if (FLogger.Logger is null)
            return;

        FLogger.Append(ELog.Information, () =>
        {
            FLogger.Text($"Container export started: {containerName} ({packageCount} package(s)) -> ", FModel.Constants.WHITE);
            FLogger.Link(Path.GetFileName(destinationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), destinationDirectory, true);
        });
    }

    internal static void ReportContainerPackageFailure(string containerName, string packagePath, Exception error)
    {
        if (FLogger.Logger is null)
            return;

        FLogger.Append(ELog.Error, () =>
            FLogger.Text($"Container export package failed: {containerName} / {packagePath}: {error.Message}", FModel.Constants.WHITE, true));
    }

    internal static void ReportContainerCompleted(
        string containerName,
        string destinationDirectory,
        int packageCount,
        int failedPackageCount,
        int resultCount,
        int failedResultCount)
    {
        if (FLogger.Logger is null)
            return;

        var succeeded = failedPackageCount == 0 && failedResultCount == 0;
        FLogger.Append(succeeded ? ELog.Information : ELog.Error, () =>
        {
            var status = succeeded ? "completed" : "completed with errors";
            FLogger.Text(
                $"Container export {status}: {containerName}; packages={packageCount}, results={resultCount}, " +
                $"packageFailures={failedPackageCount}, resultFailures={failedResultCount} -> ",
                FModel.Constants.WHITE);
            FLogger.Link(Path.GetFileName(destinationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), destinationDirectory, true);
        });
    }

    internal static void ReportContainerFailed(string containerName, string destinationDirectory, Exception error)
    {
        if (FLogger.Logger is null)
            return;

        FLogger.Append(ELog.Error, () =>
            FLogger.Text($"Container export failed: {containerName} -> {destinationDirectory}: {error.Message}", FModel.Constants.WHITE, true));
    }

    internal static void ReportBatchCompleted(FModelV3ContainerBatchRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (FLogger.Logger is null)
            return;

        var succeeded = summary.Succeeded;
        FLogger.Append(succeeded ? ELog.Information : ELog.Error, () =>
            FLogger.Text(
                $"Container batch export {(succeeded ? "completed" : "completed with errors")}: " +
                $"containers={summary.ContainerCount}, failed={summary.FailedContainerCount}, " +
                $"v3={summary.V3Enabled}, materialMode={summary.MaterialMode}",
                FModel.Constants.WHITE,
                true));
    }

    internal static void Report(IReadOnlyList<ExportResult> results, string exportDirectory, bool isUeFormatSession,
        string operationId)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        var ownedResults = FModelV3ExporterFactory.TakeOwnedResults(results);
        if (ownedResults.Count == 0 || !isUeFormatSession)
            return;

        var ownedObjectPaths = ownedResults
            .Select(static result => result.ObjectPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failedOwnedResults = ownedResults.Where(static result => !result.Success).ToArray();
        var otherFailedResults = results
            .Where(result => !result.Success && !ownedObjectPaths.Contains(result.ObjectPath))
            .ToArray();
        var diskFiles = ownedResults
            .SelectMany(static result => result.DiskFilePaths ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var modelFile = diskFiles
            .FirstOrDefault(static path => string.Equals(Path.GetExtension(path), ".uemodel", StringComparison.OrdinalIgnoreCase));
        var linkTarget = modelFile ?? (Directory.Exists(exportDirectory) ? exportDirectory : null);
        var succeeded = failedOwnedResults.Length == 0;

        if (FLogger.Logger is null)
            return;

        FLogger.Append(succeeded ? ELog.Information : ELog.Error, () =>
        {
            var message = succeeded
                ? $"Operation {operationId}: UEFormat V3 export completed: {ownedResults.Count} model(s), {diskFiles.Length} file(s) written"
                : $"Operation {operationId}: UEFormat V3 export completed with errors: {ownedResults.Count - failedOwnedResults.Length}/{ownedResults.Count} model(s) succeeded";
            FLogger.Text(message, FModel.Constants.WHITE);
            if (!string.IsNullOrWhiteSpace(linkTarget))
            {
                FLogger.Text(" -> ", FModel.Constants.GRAY);
                FLogger.Link(Path.GetFileName(linkTarget.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), linkTarget, true);
            }
            else
            {
                FLogger.Text(string.Empty, FModel.Constants.WHITE, true);
            }
        });

        foreach (var failure in failedOwnedResults.Take(5))
        {
            FLogger.Append(ELog.Error, () =>
                FLogger.Text($"{failure.ObjectPath}: {failure.Error?.Message ?? "Export failed."}", FModel.Constants.WHITE, true));
        }

        if (otherFailedResults.Length > 0)
        {
            FLogger.Append(ELog.Warning, () =>
                FLogger.Text(
                    $"Export Session also reported {otherFailedResults.Length} other/dependency error(s). Open Export Session for details.",
                    FModel.Constants.WHITE,
                    true));
        }
    }

    internal static void ReportCanceled(string operationId = "")
    {
        if (!FModelV3ExporterFactory.ClearPending() || FLogger.Logger is null)
            return;

        FLogger.Append(ELog.Warning, () =>
            FLogger.Text(string.IsNullOrWhiteSpace(operationId)
                ? "UEFormat V3 export canceled."
                : $"Operation {operationId}: UEFormat V3 export canceled.", FModel.Constants.WHITE, true));
    }
}
