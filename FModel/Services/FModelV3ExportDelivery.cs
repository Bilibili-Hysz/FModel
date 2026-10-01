#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using CUE4Parse.FModelUEFormat.ExportPipeline.Delivery;

namespace FModel.Services;

/// <summary>Operation-scoped delivery metadata; never keeps exporter/UObject/image buffers alive.</summary>
internal static class FModelV3ExportDelivery
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, FModelUeFormatDeliveryDeclaration>> Pending =
        new(StringComparer.OrdinalIgnoreCase);

    internal static void Record(string operationId, FModelUeFormatDeliveryDeclaration declaration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(declaration);
        Pending.GetOrAdd(operationId, static _ => new(StringComparer.OrdinalIgnoreCase))[declaration.RootObjectPath] = declaration;
    }

    internal static IReadOnlyList<FModelUeFormatDeliveryDeclaration> Take(string operationId) =>
        Pending.TryRemove(operationId, out var declarations)
            ? declarations.Values.ToArray()
            : Array.Empty<FModelUeFormatDeliveryDeclaration>();

    internal static void Clear(string operationId)
    {
        if (!string.IsNullOrWhiteSpace(operationId)) Pending.TryRemove(operationId, out _);
    }
}
