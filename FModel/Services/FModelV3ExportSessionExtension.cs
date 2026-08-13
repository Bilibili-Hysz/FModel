#nullable enable

using System;
using CUE4Parse;
using CUE4Parse.FModelUEFormat.ExportPipeline.Diagnostics;
using CUE4Parse.FModelUEFormat.ExportPipeline.Formats;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Exporters;
using CUE4Parse_Conversion.Formats.Meshes;
using CUE4Parse_Conversion.Options;
using FModel.ViewModels;

namespace FModel.Services;

/// <summary>
/// Supplies the FModel-owned UEFormat mesh decorator without replacing any
/// official root exporter. World export intentionally remains fully upstream
/// owned: UWorld + USD uses the official WorldExporter/UsdWorldFormat path,
/// while UWorld + UEFormat receives the official unsupported-format result.
/// </summary>
internal sealed class FModelV3ExportSessionExtension : IExportSessionExtension
{
    public static FModelV3ExportSessionExtension Current { get; } = new();

    private FModelV3ExportSessionExtension()
    {
    }

    public IMeshExportFormat? TryCreateMeshFormat(
        UObject export,
        EMeshFormat format,
        ExportSession session)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(session);

        // Fail closed outside the agreed resource-level scope. ActorX, GLTF,
        // USD, Skeleton and every non-mesh UObject remain entirely upstream.
        if (format != EMeshFormat.UEFormat ||
            !FModelV3ExporterFactory.IsSupported(export))
            return null;

        var policy = FModelV3ExporterFactory.CreatePolicySnapshot();
        if (!policy.Enabled)
            return null;

        return new FModelUeFormatMeshFormat(policy, session, export, ReportDiagnostic);
    }

    private static void ReportDiagnostic(FModelUeFormatPipelineDiagnostic diagnostic)
    {
        var visibleContext = "object=" + diagnostic.ObjectPath;
        if (!string.IsNullOrWhiteSpace(diagnostic.MaterialSlot))
            visibleContext += ", material_slot=" + diagnostic.MaterialSlot;
        if (!string.IsNullOrWhiteSpace(diagnostic.TextureParameter))
            visibleContext += ", texture_parameter=" + diagnostic.TextureParameter;

        var logger = CUE4ParseLog.Log
            .ForContext("DiagnosticCode", diagnostic.Code)
            .ForContext("PipelineStage", diagnostic.Stage)
            .ForContext("Recovery", diagnostic.Recovery)
            .ForContext("ObjectPath", diagnostic.ObjectPath)
            .ForContext("MaterialSlot", diagnostic.MaterialSlot)
            .ForContext("TextureParameter", diagnostic.TextureParameter)
            .ForContext("EvidenceGeneration", diagnostic.EvidenceGeneration)
            .ForContext("DiagnosticContext", visibleContext);
        switch (diagnostic.Severity)
        {
            case FModelUeFormatDiagnosticSeverity.Error:
                logger.Error("{DiagnosticCode}: {DiagnosticMessage} [{DiagnosticContext}]", diagnostic.Code, diagnostic.Message, visibleContext);
                break;
            case FModelUeFormatDiagnosticSeverity.Warning:
                logger.Warning("{DiagnosticCode}: {DiagnosticMessage} [{DiagnosticContext}]", diagnostic.Code, diagnostic.Message, visibleContext);
                break;
            default:
                logger.Information("{DiagnosticCode}: {DiagnosticMessage} [{DiagnosticContext}]", diagnostic.Code, diagnostic.Message, visibleContext);
                break;
        }
    }
}
