#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace FModel.Framework;

internal sealed record FModelV3ContainerRawFileFailure(
    string FilePath,
    string Error);

internal sealed record FModelV3ContainerRawRunSummary(
    string ContainerPath,
    string ContainerName,
    string DestinationDirectory,
    int CandidateFileCount,
    int ExportedFileCount,
    IReadOnlyList<FModelV3ContainerRawFileFailure> Failures,
    string? Error = null)
{
    public bool Succeeded => Error is null && Failures.Count == 0;
}

internal sealed record FModelV3ContainerRawBatchRunSummary(
    FModelV3ContainerExportDestination Destination,
    IReadOnlyList<FModelV3ContainerRawRunSummary> Containers)
{
    public int ContainerCount => Containers.Count;
    public int FailedContainerCount => Containers.Count(static container => !container.Succeeded);
    public int CandidateFileCount => Containers.Sum(static container => container.CandidateFileCount);
    public int ExportedFileCount => Containers.Sum(static container => container.ExportedFileCount);
    public int FailedFileCount => Containers.Sum(static container => container.Failures.Count);
    public bool Succeeded => Containers.Count > 0 && FailedContainerCount == 0;
}
