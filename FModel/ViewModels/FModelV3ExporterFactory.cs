#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.GeometryCollection;
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
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> PendingOwnedObjectPaths =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool IsEnabled => true;

    internal static bool IsSupported(UObject export) => FModelV3MeshEligibility.SupportsMaterialProjection(export);

    internal static FModelUeFormatPolicy CreatePolicySnapshot()
    {
        return FModelV3ContainerExportScope.CurrentPolicy
            ?? new FModelUeFormatPolicy(true, FModelV3ExportSessionState.Current.MaterialMode);
    }

    internal static bool TrackQueued(UObject export, string operationId)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        if (!IsSupported(export))
            return false;

        PendingOwnedObjectPaths
            .GetOrAdd(operationId, static _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase))
            .TryAdd(export.GetPathName(), 0);
        return true;
    }

    internal static void ForgetOwnedObjectPath(string operationId, string objectPath)
    {
        if (!string.IsNullOrWhiteSpace(operationId)
            && !string.IsNullOrWhiteSpace(objectPath)
            && PendingOwnedObjectPaths.TryGetValue(operationId, out var ownedPaths))
        {
            ownedPaths.TryRemove(objectPath, out _);
        }
    }

    internal static bool TryTakeOwnedResult(string operationId, string objectPath)
    {
        return !string.IsNullOrWhiteSpace(operationId)
            && !string.IsNullOrWhiteSpace(objectPath)
            && PendingOwnedObjectPaths.TryGetValue(operationId, out var ownedPaths)
            && ownedPaths.TryRemove(objectPath, out _);
    }

    internal static bool ClearPending(string operationId)
    {
        FModelV3ExportDelivery.Clear(operationId);
        return !string.IsNullOrWhiteSpace(operationId)
            && PendingOwnedObjectPaths.TryRemove(operationId, out var ownedPaths)
            && !ownedPaths.IsEmpty;
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
