#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace FModel.Framework;

/// <summary>
/// New-pipeline batch planner migrated from the old container branch.
/// It intentionally stops before export execution: CB3 must provide an explicit
/// ExportSession destination/run seam before any package is queued or written.
/// </summary>
internal static class FModelV3ContainerBatchExportService
{
    public static FModelV3ContainerBatchPlan CreatePlan(
        FModelV3ContainerExportRequest request,
        IEnumerable<FModelV3ContainerPackageCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidates);

        var selectedPaths = request.SelectedContainerPaths
            .Select(FModelV3ContainerIdentity.NormalizeContainerPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var duplicateBasenames = FModelV3ContainerIdentity.HasDuplicateBasenames(selectedPaths);
        if (request.RejectDuplicateBasenames && duplicateBasenames)
            throw new InvalidOperationException("Output export is blocked when selected containers share a basename.");

        var packagesByContainer = candidates
            .Where(static candidate => candidate.IsSupported)
            .GroupBy(static candidate => candidate.NormalizedContainerPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static candidate => candidate.PackagePath)
                    .Where(static path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var entries = selectedPaths
            .Select(path =>
            {
                var name = FModelV3ContainerIdentity.GetContainerName(path);
                var sourceLabel = request.GameDirectory is null
                    ? System.IO.Directory.GetParent(path)?.FullName ?? path
                    : FModelV3ContainerIdentity.GetSourceDirectoryLabel(path, request.GameDirectory);

                return new FModelV3ContainerPlanEntry(
                    path,
                    name,
                    FModelV3ContainerIdentity.GetContainerDirectoryName(name),
                    sourceLabel,
                    packagesByContainer.TryGetValue(path, out var packages) ? packages : []);
            })
            .ToArray();

        return new(entries, request.Destination, duplicateBasenames);
    }
}
