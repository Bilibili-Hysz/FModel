#nullable enable

using System;
using System.IO;
using CUE4Parse.UE4.IO;
using CUE4Parse.UE4.Pak;
using CUE4Parse.UE4.VirtualFileSystem;

namespace FModel.Framework;

/// <summary>
/// Describes the container boundary that FModel can expose to the user.
/// Sidecar files and OnDemand descriptors are deliberately not treated as
/// independent mounted containers; their mounted readers are the selectable
/// unit instead.
/// </summary>
internal enum FModelV3ContainerFormat
{
    Unknown,
    Pak,
    IoStore,
    IoStoreOnDemand,
    IoStoreOnDemandDescriptor,
    IoStoreDataSidecar,
    SignatureSidecar,
}

internal static class FModelV3ContainerFormats
{
    public static FModelV3ContainerFormat ClassifyReader(IAesVfsReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader is IoStoreOnDemandReader)
            return FModelV3ContainerFormat.IoStoreOnDemand;
        if (reader is IoStoreReader)
            return FModelV3ContainerFormat.IoStore;
        if (reader is PakFileReader)
            return FModelV3ContainerFormat.Pak;

        // Several game-specific providers expose custom reader subclasses.
        // Their physical extension remains the stable fallback discriminator.
        return ClassifyPath(reader.Name);
    }

    public static FModelV3ContainerFormat ClassifyPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return FModelV3ContainerFormat.Unknown;

        return Path.GetExtension(path).TrimStart('.').ToUpperInvariant() switch
        {
            "PAK" or "UPAK" => FModelV3ContainerFormat.Pak,
            "UTOC" => FModelV3ContainerFormat.IoStore,
            "UONDEMANDTOC" => FModelV3ContainerFormat.IoStoreOnDemandDescriptor,
            "UCAS" => FModelV3ContainerFormat.IoStoreDataSidecar,
            "SIG" => FModelV3ContainerFormat.SignatureSidecar,
            _ => FModelV3ContainerFormat.Unknown,
        };
    }

    /// <summary>
    /// Returns true for a mounted reader that can be shown as a selectable
    /// container. Optional/OnDemand names are no longer hidden here: if the
    /// provider mounted a real reader, the user can inspect/export it.
    /// </summary>
    public static bool ShouldDisplayReader(IAesVfsReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.Name.StartsWith("global", StringComparison.OrdinalIgnoreCase))
            return false;

        return ClassifyReader(reader) is
            FModelV3ContainerFormat.Pak or
            FModelV3ContainerFormat.IoStore or
            FModelV3ContainerFormat.IoStoreOnDemand;
    }

    public static bool IsSelectablePath(string path)
        => ClassifyPath(path) is FModelV3ContainerFormat.Pak or FModelV3ContainerFormat.IoStore;

    public static bool IsDescriptorPath(string path)
        => ClassifyPath(path) == FModelV3ContainerFormat.IoStoreOnDemandDescriptor;

    public static bool IsSidecarPath(string path)
        => ClassifyPath(path) is FModelV3ContainerFormat.IoStoreDataSidecar or FModelV3ContainerFormat.SignatureSidecar;

    public static bool IsOnDemand(IAesVfsReader reader)
        => ClassifyReader(reader) == FModelV3ContainerFormat.IoStoreOnDemand;

    /// <summary>
    /// Source-directory export is only safe when the reader points to a real
    /// local container. Streamed and OnDemand readers use synthetic paths.
    /// </summary>
    public static bool CanExportToSourceDirectory(IAesVfsReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return !IsOnDemand(reader) && File.Exists(reader.Path);
    }

    public static string GetDisplayLabel(IAesVfsReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (IsOnDemand(reader))
            return "IoStore OnDemand";

        return Path.GetExtension(reader.Name).TrimStart('.').ToUpperInvariant() switch
        {
            "UPAK" => "UPAK",
            "UTOC" => "IoStore",
            "PAK" => "PAK",
            _ => "UE container",
        };
    }

    public static string GetDisplayDescription(IAesVfsReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var label = GetDisplayLabel(reader);
        if (CanExportToSourceDirectory(reader))
            return $"{label}; local container; asset export and raw file extraction available.";
        if (IsOnDemand(reader))
            return $"{label}; streamed reader; export to Output is available, source-directory export is blocked.";
        return $"{label}; virtual or streamed reader; source-directory export is unavailable.";
    }
}
