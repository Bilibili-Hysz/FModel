#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
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

    internal static void ReportContainerRouteUnavailable(string message)
    {
        if (FLogger.Logger is null)
            return;

        FLogger.Append(ELog.Warning, () =>
            FLogger.Text(message, FModel.Constants.WHITE, true));
    }

    internal static void ReportRawContainerStarted(string containerName, string destinationDirectory)
    {
        if (FLogger.Logger is null)
            return;

        FLogger.Append(ELog.Information, () =>
        {
            FLogger.Text($"Raw container extraction started: {containerName} -> ", FModel.Constants.WHITE);
            FLogger.Link(Path.GetFileName(destinationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), destinationDirectory, true);
        });
    }

    internal static void ReportRawFileFailure(string containerName, string filePath, Exception error)
    {
        if (FLogger.Logger is null)
            return;

        FLogger.Append(ELog.Error, () =>
            FLogger.Text($"Raw container file failed: {containerName} / {filePath}: {error.Message}", FModel.Constants.WHITE, true));
    }

    internal static void ReportRawContainerCompleted(FModelV3ContainerRawRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (FLogger.Logger is null)
            return;

        var succeeded = summary.Succeeded;
        FLogger.Append(succeeded ? ELog.Information : ELog.Error, () =>
        {
            var status = succeeded ? "completed" : "completed with errors";
            FLogger.Text(
                $"Raw container extraction {status}: {summary.ContainerName}; files={summary.ExportedFileCount}/{summary.CandidateFileCount}, failures={summary.Failures.Count} -> ",
                FModel.Constants.WHITE);
            FLogger.Link(Path.GetFileName(summary.DestinationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), summary.DestinationDirectory, true);
        });
    }

    internal static void ReportRawBatchCompleted(FModelV3ContainerRawBatchRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (FLogger.Logger is null)
            return;

        var succeeded = summary.Succeeded;
        FLogger.Append(succeeded ? ELog.Information : ELog.Error, () =>
            FLogger.Text(
                $"Raw container batch extraction {(succeeded ? "completed" : "completed with errors")}: " +
                $"containers={summary.ContainerCount}, files={summary.ExportedFileCount}/{summary.CandidateFileCount}, failures={summary.FailedFileCount}",
                FModel.Constants.WHITE,
                true));
    }

    internal static void Report(IReadOnlyList<ExportResult> results, string exportDirectory, bool isUeFormatSession,
        string operationId)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        if (!isUeFormatSession)
        {
            FModelV3ExporterFactory.ClearPending(operationId);
            return;
        }

        // Consume ownership markers in one pass. The previous implementation built
        // an owned-result list, an owned-path HashSet and multiple arrays over the
        // complete result set. For a large container batch those transient
        // projections doubled the lifetime of every result/path string just while
        // printing the terminal summary.
        var ownedCount = 0;
        var failedOwnedCount = 0;
        var otherFailedCount = 0;
        var modelFile = default(string);
        var diskFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var firstFailures = new List<(string ObjectPath, string Message)>(capacity: 5);
        foreach (var result in results)
        {
            if (!FModelV3ExporterFactory.TryTakeOwnedResult(operationId, result.ObjectPath))
            {
                if (!result.Success) otherFailedCount++;
                continue;
            }

            ownedCount++;
            if (!result.Success)
            {
                failedOwnedCount++;
                if (firstFailures.Count < 5)
                {
                    firstFailures.Add((result.ObjectPath, result.Error?.Message ?? "Export failed."));
                }
            }

            foreach (var path in result.DiskFilePaths ?? Array.Empty<string>())
            {
                if (diskFiles.Add(path)
                    && modelFile is null
                    && string.Equals(Path.GetExtension(path), ".uemodel", StringComparison.OrdinalIgnoreCase))
                {
                    modelFile = path;
                }
            }
        }

        // A terminal report owns the complete operation. Drop any root markers
        // that did not receive a result because a host/exporter failed unusually.
        FModelV3ExporterFactory.ClearPending(operationId);

        if (ownedCount == 0)
            return;

        var linkTarget = modelFile ?? (Directory.Exists(exportDirectory) ? exportDirectory : null);
        var succeeded = failedOwnedCount == 0;

        if (FLogger.Logger is null)
            return;

        FLogger.Append(succeeded ? ELog.Information : ELog.Error, () =>
        {
            var message = succeeded
                ? $"Operation {operationId}: UEFormat V3 export completed: {ownedCount} model(s), {diskFiles.Count} file(s) written"
                : $"Operation {operationId}: UEFormat V3 export completed with errors: {ownedCount - failedOwnedCount}/{ownedCount} model(s) succeeded";
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

        foreach (var failure in firstFailures)
        {
            FLogger.Append(ELog.Error, () =>
                FLogger.Text($"{failure.ObjectPath}: {failure.Message}", FModel.Constants.WHITE, true));
        }

        if (otherFailedCount > 0)
        {
            FLogger.Append(ELog.Warning, () =>
                FLogger.Text(
                    $"Export Session also reported {otherFailedCount} other/dependency error(s). Open Export Session for details.",
                    FModel.Constants.WHITE,
                    true));
        }
    }

    internal static void ReportCanceled(string operationId)
    {
        if (!FModelV3ExporterFactory.ClearPending(operationId) || FLogger.Logger is null)
            return;

        FLogger.Append(ELog.Warning, () =>
            FLogger.Text($"Operation {operationId}: UEFormat V3 export canceled.", FModel.Constants.WHITE, true));
    }
}
