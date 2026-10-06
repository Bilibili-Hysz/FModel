#nullable enable
using CUE4Parse.FModelUEFormat.ExportPipeline.Diagnostics;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.GeometryCollection;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse_Conversion.Options;

namespace FModel.Services;

/// <summary>Pure eligibility rules shared by queue tracking and format diagnostics.</summary>
internal static class FModelV3MeshEligibility
{
    internal static bool SupportsMaterialProjection(UObject export) =>
        export is UStaticMesh or USkeletalMesh or UGeometryCollection;

    internal static FModelUeFormatPipelineDiagnostic? StandardOnlyDiagnostic(
        UObject export, EMeshFormat format, FModelUeFormatPolicy policy)
    {
        if (format != EMeshFormat.UEFormat || !policy.Enabled
            || policy.MaterialMode == FModelUeFormatMaterialMode.Disabled
            || export is USkeleton || SupportsMaterialProjection(export))
            return null;

        return new FModelUeFormatPipelineDiagnostic(
            "material.type.standard-only", FModelUeFormatDiagnosticSeverity.Warning,
            FModelUeFormatPipelineStage.Eligibility, export.GetPathName(),
            $"Mesh type {export.GetType().Name} uses the official standard-only UEFormat exporter; FModel material/texture attributes are unavailable and Strict material completeness does not apply to this type.",
            recovery: "Use a supported StaticMesh, SkeletalMesh or GeometryCollection for FModel material links, or assign the standard-only model's materials manually.",
            evidenceGeneration: policy.EvidenceGeneration);
    }
}
