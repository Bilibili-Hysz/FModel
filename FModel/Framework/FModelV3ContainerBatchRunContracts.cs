#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Options;

namespace FModel.Framework;

/// <summary>
/// Immutable runtime inputs for a container batch. The output root and
/// ExportOptions are captured by the caller before execution; the runner never
/// changes UserSettings to steer an export.
/// </summary>
internal sealed record FModelV3ContainerBatchRunRequest(
    FModelV3ContainerExportRequest Selection,
    string OutputRoot,
    ExportOptions ExportOptions)
{
    public static FModelV3ContainerBatchRunRequest Create(
        FModelV3ContainerExportRequest selection,
        string outputRoot,
        ExportOptions exportOptions)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(exportOptions);
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("An explicit output root is required.", nameof(outputRoot));

        return new(selection, Path.GetFullPath(outputRoot.Trim()), exportOptions);
    }
}

internal sealed record FModelV3ContainerPackageFailure(
    string PackagePath,
    string Error);

internal sealed record FModelV3ContainerRunSummary(
    string ContainerPath,
    string ContainerName,
    string DestinationDirectory,
    int PackageCount,
    int FailedPackageCount,
    int ResultCount,
    int FailedResultCount,
    IReadOnlyList<FModelV3ContainerPackageFailure> PackageFailures,
    IReadOnlyList<ExportResult> Results,
    string? Error = null)
{
    public bool Succeeded => Error is null && FailedPackageCount == 0 && FailedResultCount == 0;
}

internal sealed record FModelV3ContainerBatchRunSummary(
    FModelV3ContainerExportDestination Destination,
    IReadOnlyList<FModelV3ContainerRunSummary> Containers,
    bool V3Enabled,
    string MaterialMode)
{
    public int ContainerCount => Containers.Count;
    public int FailedContainerCount => Containers.Count(container => !container.Succeeded);
    public bool Succeeded => FailedContainerCount == 0;
}
