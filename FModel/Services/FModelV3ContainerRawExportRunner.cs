#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CUE4Parse.UE4.VirtualFileSystem;
using FModel.Framework;
using FModel.ViewModels;
using Serilog;

namespace FModel.Services;

/// <summary>
/// Separate raw-file extraction route for selected mounted containers.
/// It deliberately does not enter ExportSession or UEFormat; it copies the
/// decompressed virtual files owned by each selected reader to a safe tree.
/// </summary>
internal static class FModelV3ContainerRawExportRunner
{
    internal static async Task<FModelV3ContainerRawBatchRunSummary> RunAsync(
        CUE4ParseViewModel host,
        FModelV3ContainerExportRequest request,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        var mountedReaders = host.Provider.MountedVfs
            .Select(reader => (Reader: reader, Path: FModelV3ContainerIdentity.NormalizeContainerPath(reader.Path)))
            .GroupBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Reader, StringComparer.OrdinalIgnoreCase);

        var selectedReaders = new Dictionary<string, IAesVfsReader>(StringComparer.OrdinalIgnoreCase);
        foreach (var selectedPath in request.SelectedContainerPaths)
        {
            var normalizedPath = FModelV3ContainerIdentity.NormalizeContainerPath(selectedPath);
            if (!mountedReaders.TryGetValue(normalizedPath, out var reader))
                throw new InvalidOperationException($"Selected container is no longer mounted: {normalizedPath}");

            selectedReaders[normalizedPath] = reader;
        }

        var selectedPaths = selectedReaders.Keys.ToArray();
        if (request.RejectDuplicateBasenames && FModelV3ContainerIdentity.HasDuplicateBasenames(selectedPaths))
            throw new InvalidOperationException("Output raw extraction is blocked when selected containers share a basename.");

        var summaries = new List<FModelV3ContainerRawRunSummary>(selectedReaders.Count);
        foreach (var (containerPath, reader) in selectedReaders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var containerName = FModelV3ContainerIdentity.GetContainerName(containerPath);
            var containerDirectoryName = FModelV3ContainerIdentity.GetContainerDirectoryName(containerName);
            var destinationDirectory = ResolveDestination(request, outputRoot, reader, containerDirectoryName);
            Directory.CreateDirectory(destinationDirectory);
            FModelV3ExportFeedback.ReportRawContainerStarted(containerName, destinationDirectory);

            var failures = new List<FModelV3ContainerRawFileFailure>();
            var candidates = reader.Files.Values
                .OfType<VfsEntry>()
                .Where(file => ReferenceEquals(file.Vfs, reader))
                .GroupBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .OrderBy(static file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var exportedFileCount = 0;
            foreach (var file in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var destinationPath = FModelV3ContainerIdentity.CombineContainedPath(destinationDirectory, file.Path);
                    var parent = Path.GetDirectoryName(destinationPath);
                    if (string.IsNullOrWhiteSpace(parent))
                        throw new InvalidOperationException("Resolved raw output path has no parent directory.");

                    Directory.CreateDirectory(parent);
                    var data = reader.Extract(file);
                    await File.WriteAllBytesAsync(destinationPath, data, cancellationToken).ConfigureAwait(false);
                    exportedFileCount++;
                    Log.Information("v3.container.raw exported {ContainerPath} {VirtualPath} -> {DestinationPath}",
                        containerPath, file.Path, destinationPath);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception error)
                {
                    failures.Add(new(file.Path, error.Message));
                    Log.Error(error, "v3.container.raw file failed {ContainerPath} {VirtualPath}", containerPath, file.Path);
                    FModelV3ExportFeedback.ReportRawFileFailure(containerName, file.Path, error);
                }
            }

            var summary = new FModelV3ContainerRawRunSummary(
                containerPath,
                containerName,
                destinationDirectory,
                candidates.Length,
                exportedFileCount,
                failures);
            summaries.Add(summary);
            FModelV3ExportFeedback.ReportRawContainerCompleted(summary);
        }

        var batchSummary = new FModelV3ContainerRawBatchRunSummary(request.Destination, summaries);
        FModelV3ExportFeedback.ReportRawBatchCompleted(batchSummary);
        return batchSummary;
    }

    private static string ResolveDestination(
        FModelV3ContainerExportRequest request,
        string outputRoot,
        IAesVfsReader reader,
        string containerDirectoryName)
    {
        string root;
        if (request.Destination == FModelV3ContainerExportDestination.Output)
        {
            root = Path.Combine(outputRoot, "RawExports");
        }
        else
        {
            if (!FModelV3ContainerFormats.CanExportToSourceDirectory(reader))
                throw new InvalidOperationException(
                    $"Source-directory raw extraction is unavailable for virtual or streamed container '{reader.Name}'. Use the Output destination instead.");

            root = Path.Combine(
                Path.GetDirectoryName(FModelV3ContainerIdentity.NormalizeContainerPath(reader.Path)) ?? outputRoot,
                "RawExports");
        }

        return FModelV3ContainerIdentity.CombineContainedDirectory(root, containerDirectoryName);
    }
}
