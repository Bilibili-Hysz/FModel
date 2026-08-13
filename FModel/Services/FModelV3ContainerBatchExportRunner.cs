#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.UE4.VirtualFileSystem;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Options;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse_Conversion.Exporters;
using FModel.Framework;
using FModel.ViewModels;
using Serilog;

namespace FModel.Services;

/// <summary>
/// Runtime host adapter for the migrated container planner. It discovers only
/// packages owned by the selected mounted reader, enqueues them through the
/// existing Extract/CheckExport/SaveExport route, and executes one explicit
/// ExportSession per container in selection order.
/// </summary>
internal static class FModelV3ContainerBatchExportRunner
{
    private const EBulkType PropertiesPass = EBulkType.Properties | EBulkType.Auto;
    private const EBulkType AssetPass = EBulkType.Meshes | EBulkType.Textures | EBulkType.Animations | EBulkType.Auto;

    internal static async Task<FModelV3ContainerBatchRunSummary> RunAsync(
        CUE4ParseViewModel host,
        FModelV3ContainerBatchRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(request);

        var mountedReaders = host.Provider.MountedVfs
            .Select(reader => (Reader: reader, Path: FModelV3ContainerIdentity.NormalizeContainerPath(reader.Path)))
            .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Reader, StringComparer.OrdinalIgnoreCase);

        var selectedReaders = new Dictionary<string, IAesVfsReader>(StringComparer.OrdinalIgnoreCase);
        foreach (var selectedPath in request.Selection.SelectedContainerPaths)
        {
            var normalizedPath = FModelV3ContainerIdentity.NormalizeContainerPath(selectedPath);
            if (!mountedReaders.TryGetValue(normalizedPath, out var reader))
                throw new InvalidOperationException($"Selected container is no longer mounted: {normalizedPath}");

            selectedReaders[normalizedPath] = reader;
        }

        var packageLookup = new Dictionary<string, IReadOnlyDictionary<string, VfsEntry>>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<FModelV3ContainerPackageCandidate>();
        foreach (var (containerPath, reader) in selectedReaders)
        {
            var ownedPackages = reader.Files.Values
                .OfType<VfsEntry>()
                .Where(file => ReferenceEquals(file.Vfs, reader) && FModelV3ContainerIdentity.IsPackageExtension(file.Extension))
                .OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            packageLookup[containerPath] = ownedPackages
                .GroupBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

            candidates.AddRange(ownedPackages.Select(file => new FModelV3ContainerPackageCandidate(containerPath, file.Path)));
        }

        var plan = FModelV3ContainerBatchExportService.CreatePlan(request.Selection, candidates);
        using var batchLease = await FModelV3ContainerExportScope.EnterBatchAsync(cancellationToken);
        var isUeFormatSession = request.ExportOptions.MeshFormat == EMeshFormat.UEFormat;
        var effectiveV3Enabled = batchLease.Policy.Enabled && isUeFormatSession;
        var effectiveMaterialMode = effectiveV3Enabled
            ? batchLease.Policy.MaterialMode.ToString()
            : FModelUeFormatMaterialMode.Disabled.ToString();
        var summaries = new List<FModelV3ContainerRunSummary>(plan.Entries.Count);

        try
        {
            foreach (var entry in plan.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reader = selectedReaders[entry.ContainerPath];
                var destinationDirectory = ResolveDestination(request, entry, reader);
                Directory.CreateDirectory(destinationDirectory);
                FModelV3ExportFeedback.ReportContainerStarted(entry.ContainerName, destinationDirectory, entry.PackagePaths.Count);

                var packageFailures = new List<FModelV3ContainerPackageFailure>();
                var results = Array.Empty<ExportResult>();
                ExportSession? session = null;

                try
                {
                    session = new ExportSession { Extension = FModelV3ExportSessionExtension.Current };
                    using (FModelV3ContainerExportScope.Push(session, batchLease.Policy))
                    {
                        foreach (var packagePath in entry.PackagePaths)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!packageLookup[entry.ContainerPath].TryGetValue(packagePath, out var package))
                            {
                                var missing = new InvalidOperationException($"Owned package disappeared before export: {packagePath}");
                                packageFailures.Add(new(packagePath, missing.Message));
                                FModelV3ExportFeedback.ReportContainerPackageFailure(entry.ContainerName, packagePath, missing);
                                continue;
                            }

                            try
                            {
                                host.Extract(cancellationToken, package, false, PropertiesPass);
                                host.Extract(cancellationToken, package, false, AssetPass);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception error)
                            {
                                packageFailures.Add(new(packagePath, error.Message));
                                Log.Error(error, "v3.container.batch package failed {ContainerPath} {PackagePath}", entry.ContainerPath, packagePath);
                                FModelV3ExportFeedback.ReportContainerPackageFailure(entry.ContainerName, packagePath, error);
                            }
                        }

                        if (session.HasQueuedItems)
                        {
                            results = (await ExportSessionViewModel.RunExplicitAsync(
                                session,
                                destinationDirectory,
                                request.ExportOptions,
                                cancellationToken: cancellationToken)).ToArray();
                        }
                    }

                    FModelV3ExportFeedback.Report(results, destinationDirectory, effectiveV3Enabled, session.OperationId);
                    var failedResultCount = results.Count(static result => !result.Success);
                    var summary = new FModelV3ContainerRunSummary(
                        entry.ContainerPath,
                        entry.ContainerName,
                        destinationDirectory,
                        entry.PackagePaths.Count,
                        packageFailures.Count,
                        results.Length,
                        failedResultCount,
                        packageFailures,
                        results);
                    summaries.Add(summary);
                    FModelV3ExportFeedback.ReportContainerCompleted(
                        entry.ContainerName,
                        destinationDirectory,
                        summary.PackageCount,
                        summary.FailedPackageCount,
                        summary.ResultCount,
                        summary.FailedResultCount);
                }
                catch (OperationCanceledException)
                {
                    FModelV3ExportFeedback.ReportCanceled(session?.OperationId ?? string.Empty);
                    throw;
                }
                catch (Exception error)
                {
                    FModelV3ExporterFactory.ClearPending();
                    Log.Error(error, "v3.container.batch container failed {ContainerPath}", entry.ContainerPath);
                    FModelV3ExportFeedback.ReportContainerFailed(entry.ContainerName, destinationDirectory, error);
                    summaries.Add(new FModelV3ContainerRunSummary(
                        entry.ContainerPath,
                        entry.ContainerName,
                        destinationDirectory,
                        entry.PackagePaths.Count,
                        packageFailures.Count,
                        results.Length,
                        results.Count(static result => !result.Success),
                        packageFailures,
                        results,
                        error.Message));
                }
                finally
                {
                    if (session?.IsRunning != true)
                        session?.Clear();
                    FModelV3ExporterFactory.ClearPending();
                }
            }
        }
        catch (OperationCanceledException)
        {
            FModelV3ExportFeedback.ReportCanceled();
            throw;
        }

        var batchSummary = new FModelV3ContainerBatchRunSummary(
            plan.Destination,
            summaries,
            effectiveV3Enabled,
            effectiveMaterialMode);
        FModelV3ExportFeedback.ReportBatchCompleted(batchSummary);
        return batchSummary;
    }

    private static string ResolveDestination(
        FModelV3ContainerBatchRunRequest request,
        FModelV3ContainerPlanEntry entry,
        IAesVfsReader reader)
    {
        var root = request.Selection.Destination == FModelV3ContainerExportDestination.Output
            ? Path.Combine(request.OutputRoot, "Exports")
            : Path.GetDirectoryName(FModelV3ContainerIdentity.NormalizeContainerPath(reader.Path)) ?? request.OutputRoot;

        return FModelV3ContainerIdentity.CombineContainedDirectory(root, entry.ContainerDirectoryName);
    }
}
