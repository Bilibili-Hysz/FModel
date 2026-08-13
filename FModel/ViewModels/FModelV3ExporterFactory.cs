#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse_Conversion;
using FModel.Services;

namespace FModel.ViewModels;

/// <summary>
/// Tracks UEFormat-capable mesh roots without replacing the official root exporter.
/// Runtime format substitution occurs only inside MeshExporter when MeshFormat is UEFormat.
/// </summary>
internal static class FModelV3ExporterFactory
{
    private const string MaterialModeVariable = "FMODEL_V3_UEFORMAT_MATERIAL_MODE";
    private static readonly ConcurrentDictionary<string, byte> PendingOwnedObjectPaths = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsEnabled => true;

    internal static bool IsSupported(UObject export) => export is UStaticMesh or USkeletalMesh;

    internal static FModelUeFormatPolicy CreatePolicySnapshot()
    {
        return FModelV3ContainerExportScope.CurrentPolicy
            ?? new FModelUeFormatPolicy(true, FModelV3ExportSessionState.Current.MaterialMode);
    }

    internal static bool TrackQueued(UObject export)
    {
        ArgumentNullException.ThrowIfNull(export);
        if (!IsSupported(export))
            return false;

        PendingOwnedObjectPaths.TryAdd(export.GetPathName(), 0);
        return true;
    }

    internal static void ForgetOwnedObjectPath(string objectPath)
    {
        if (!string.IsNullOrWhiteSpace(objectPath))
            PendingOwnedObjectPaths.TryRemove(objectPath, out _);
    }

    internal static IReadOnlyList<ExportResult> TakeOwnedResults(IReadOnlyList<ExportResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var owned = new List<ExportResult>();
        foreach (var result in results)
        {
            if (PendingOwnedObjectPaths.TryRemove(result.ObjectPath, out _))
                owned.Add(result);
        }
        return owned;
    }

    internal static bool ClearPending()
    {
        var hadPending = !PendingOwnedObjectPaths.IsEmpty;
        PendingOwnedObjectPaths.Clear();
        return hadPending;
    }

    internal static FModelUeFormatMaterialMode ReadMaterialMode()
    {
        var configured = Environment.GetEnvironmentVariable(MaterialModeVariable)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(configured))
            return FModelV3ExportSessionState.Current.MaterialMode;

        return configured switch
        {
            "strict" => FModelUeFormatMaterialMode.Strict,
            "disabled" => FModelUeFormatMaterialMode.Disabled,
            "linked" => FModelUeFormatMaterialMode.Linked,
            _ => FModelV3ExportSessionState.Current.MaterialMode
        };
    }
}
