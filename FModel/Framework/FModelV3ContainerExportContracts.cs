#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FModel.Framework;

internal enum FModelV3ContainerExportDestination
{
    Output,
    SourceDirectory,
}

internal sealed record FModelV3ContainerExportRequest(
    IReadOnlyList<string> SelectedContainerPaths,
    FModelV3ContainerExportDestination Destination,
    string? GameDirectory = null)
{
    public static FModelV3ContainerExportRequest Create(
        IEnumerable<string> selectedContainerPaths,
        FModelV3ContainerExportDestination destination,
        string? gameDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(selectedContainerPaths);

        var normalized = selectedContainerPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(FModelV3ContainerIdentity.NormalizeContainerPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
            throw new ArgumentException("At least one container path is required.", nameof(selectedContainerPaths));

        var normalizedGameDirectory = string.IsNullOrWhiteSpace(gameDirectory)
            ? null
            : FModelV3ContainerIdentity.NormalizeContainerPath(gameDirectory);

        return new(normalized, destination, normalizedGameDirectory);
    }

    public bool RejectDuplicateBasenames => Destination == FModelV3ContainerExportDestination.Output;
}

internal sealed record FModelV3ContainerPackageCandidate(
    string ContainerPath,
    string PackagePath)
{
    public string NormalizedContainerPath => FModelV3ContainerIdentity.NormalizeContainerPath(ContainerPath);

    public string Extension => Path.GetExtension(PackagePath).TrimStart('.');

    public bool IsSupported => FModelV3ContainerIdentity.IsPackageExtension(Extension);
}

internal sealed record FModelV3ContainerPlanEntry(
    string ContainerPath,
    string ContainerName,
    string ContainerDirectoryName,
    string SourceDirectoryLabel,
    IReadOnlyList<string> PackagePaths);

internal sealed record FModelV3ContainerBatchPlan(
    IReadOnlyList<FModelV3ContainerPlanEntry> Entries,
    FModelV3ContainerExportDestination Destination,
    bool HasDuplicateBasenames)
{
    public bool HasPackages => Entries.Any(static entry => entry.PackagePaths.Count > 0);

    public int PackageCount => Entries.Sum(static entry => entry.PackagePaths.Count);
}
