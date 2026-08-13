using System;
using System.Collections.Generic;
using System.Linq;
using CUE4Parse.FModelUEFormat.ExportPipeline.Diagnostics;
using CUE4Parse.FModelUEFormat.ExportPipeline.Pipeline;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UeFormat.UEModel;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Options;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.Materials;

/// <summary>
/// Deterministic material/texture projection. The upstream material pipeline remains
/// authoritative for parameter extraction and TextureExporter remains authoritative for
/// encoded output bytes; this type only maps those results to V3 records and diagnostics.
/// </summary>
public sealed class FModelMaterialProjectionBuilder
{
    public FModelMaterialProjection Build(
        string objectPath,
        int sourceMaterialCount,
        IEnumerable<FModelMaterialSlotInput> slots,
        FModelUeFormatPolicy policy,
        string? ownerModelUri = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectPath);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(policy);
        if (sourceMaterialCount < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceMaterialCount));

        if (policy.MaterialMode == FModelUeFormatMaterialMode.Disabled)
        {
            return new FModelMaterialProjection(
                objectPath,
                Array.Empty<FModelMaterialLinkProjection>(),
                Array.Empty<FModelTextureLinkProjection>(),
                [Diagnostic(objectPath, "material.projection.disabled", FModelUeFormatDiagnosticSeverity.Info, "Material projection is disabled by policy.", "Disable V3 material links")]);
        }

        var diagnostics = new List<FModelUeFormatPipelineDiagnostic>();
        var materials = new List<FModelMaterialLinkProjection>();
        var textures = new List<FModelTextureLinkProjection>();
        var resources = new Dictionary<string, FModelTextureResourceProjection>(StringComparer.Ordinal);
        var seenSlots = new HashSet<int>();
        var orderedSlots = slots.OrderBy(slot => slot.SlotIndex).ThenBy(slot => slot.SourceMaterialIndex).ToArray();

        foreach (var slot in orderedSlots)
        {
            if (!seenSlots.Add(slot.SlotIndex))
            {
                AddInvalid(diagnostics, objectPath, policy, "material.slot.duplicate", "Duplicate material slot index.", slot.SlotName);
                continue;
            }

            var valid = true;
            if (slot.SlotIndex < 0)
            {
                AddInvalid(diagnostics, objectPath, policy, "material.slot.invalid-index", "Material slot index must be non-negative.", slot.SlotName);
                valid = false;
            }
            if (slot.SourceMaterialIndex < 0 || slot.SourceMaterialIndex >= sourceMaterialCount)
            {
                AddInvalid(diagnostics, objectPath, policy, "material.slot.source-index", "Source material index is outside the mesh material array.", slot.SlotName);
                valid = false;
            }
            if (string.IsNullOrWhiteSpace(slot.SlotName))
            {
                AddInvalid(diagnostics, objectPath, policy, "material.slot.name", "Material slot name is required.", slot.SlotName);
                valid = false;
            }
            if (string.IsNullOrWhiteSpace(slot.SourceIdentity))
            {
                AddInvalid(diagnostics, objectPath, policy, "material.slot.source-identity", "Resolved Unreal material identity is required.", slot.SlotName);
                valid = false;
            }
            if (!TryNormalizeUri(
                    slot.MaterialJsonUri,
                    out var materialUri,
                    allowLeadingParents: policy.ResourceUriRoot == FModelUeFormatResourceUriRoot.RelativeToOwner))
            {
                AddInvalid(diagnostics, objectPath, policy, "material.slot.uri", "Material JSON URI is not a safe logical URI.", slot.SlotName);
                valid = false;
            }

            if (!valid)
                continue;

            materials.Add(new FModelMaterialLinkProjection(
                slot.SlotIndex,
                slot.SourceMaterialIndex,
                slot.SlotName,
                slot.SourceIdentity!,
                materialUri!));

            var seenParameters = new HashSet<string>(StringComparer.Ordinal);
            foreach (var texture in (slot.Textures ?? Array.Empty<FModelTextureInput>())
                         .OrderBy(texture => texture.ParameterIdentity, StringComparer.Ordinal)
                         .ThenBy(texture => texture.SourceIdentity, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(texture.ParameterIdentity))
                {
                    AddInvalid(diagnostics, objectPath, policy, "texture.binding.parameter", "Texture parameter identity is required.", slot.SlotName);
                    continue;
                }
                if (!seenParameters.Add(texture.ParameterIdentity))
                {
                    AddInvalid(diagnostics, objectPath, policy, "texture.binding.duplicate-parameter", "Texture parameter identity is duplicated within a material.", texture.ParameterIdentity);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(texture.SourceIdentity))
                {
                    AddInvalid(diagnostics, objectPath, policy, "texture.binding.source-identity", "Resolved Unreal texture identity is required.", texture.ParameterIdentity);
                    continue;
                }

                var outputs = (texture.Outputs ?? Array.Empty<FModelTextureOutputProjection>())
                    .OrderBy(output => output.OutputOrdinal)
                    .ThenBy(output => output.LogicalResourceUri, StringComparer.Ordinal)
                    .ThenBy(output => output.StableId, StringComparer.Ordinal)
                    .ToArray();

                if (texture.ExportMetadataAvailable == false && policy.EmitTextureLinks)
                {
                    AddInvalid(diagnostics, objectPath, policy, "texture.export.metadata", "Upstream TextureExporter output metadata is unavailable.", texture.ParameterIdentity);
                    continue;
                }

                if (outputs.Length == 0)
                {
                    // NP3 callers can still validate material links with URI-only synthetic
                    // inputs. FMODEL_TEXTURES is emitted only when NP4 metadata is present.
                    if (policy.EmitTextureLinks && texture.ExportMetadataAvailable == true)
                        AddInvalid(diagnostics, objectPath, policy, "texture.export.empty", "Upstream TextureExporter produced no output files.", texture.ParameterIdentity);
                    continue;
                }

                if (!TryClassifyMap(texture, out var mapKind))
                {
                    AddInvalid(diagnostics, objectPath, policy, "texture.binding.map-kind", "Texture parameter cannot be mapped to BaseColor, Normal, or Orm.", texture.ParameterIdentity);
                    continue;
                }

                var firstResourceId = string.Empty;
                var outputValid = true;
                foreach (var output in outputs)
                {
                    if (string.IsNullOrWhiteSpace(output.StableId)
                        || !TryNormalizeUri(
                            output.LogicalResourceUri,
                            out var outputUri,
                            allowLeadingParents: output.LocationMode == FModelTextureResourceLocation.Relative))
                    {
                        AddInvalid(diagnostics, objectPath, policy, "texture.resource.uri", "Texture output URI is not a safe logical URI.", texture.ParameterIdentity);
                        outputValid = false;
                        continue;
                    }
                    if (output.OutputOrdinal < 0 || output.ByteLength < 0 || output.Sha256 is null || output.Sha256.Count != 32)
                    {
                        AddInvalid(diagnostics, objectPath, policy, "texture.resource.integrity", "Texture output ordinal, byte length, or SHA-256 metadata is invalid.", texture.ParameterIdentity);
                        outputValid = false;
                        continue;
                    }

                    var resource = new FModelTextureResourceProjection(
                        output.StableId,
                        texture.SourceIdentity,
                        outputUri!,
                        output.ByteLength,
                        output.Sha256,
                        output.IsEmbedded,
                        output.LocationMode);
                    if (resources.TryGetValue(resource.StableId, out var existing) && !HasSameResourceMetadata(existing, resource))
                    {
                        AddInvalid(diagnostics, objectPath, policy, "texture.resource.conflict", "Texture stable ID resolves to conflicting output metadata.", texture.ParameterIdentity);
                        outputValid = false;
                        continue;
                    }
                    resources[resource.StableId] = resource;
                    if (firstResourceId.Length == 0)
                        firstResourceId = resource.StableId;
                }

                if (outputValid && firstResourceId.Length > 0 && policy.EmitTextureLinks)
                {
                    var firstUri = resources[firstResourceId].LogicalResourceUri;
                    textures.Add(new FModelTextureLinkProjection(
                        slot.SlotIndex,
                        texture.ParameterIdentity,
                        texture.SourceIdentity,
                        firstUri,
                        firstResourceId,
                        mapKind,
                        texture.IsSrgb ?? mapKind == PbrMapKind.BaseColor,
                        slot.SlotName));
                }
            }
        }

        return new FModelMaterialProjection(
            objectPath,
            materials.OrderBy(material => material.SlotIndex).ToArray(),
            textures.OrderBy(texture => texture.MaterialSlotIndex).ThenBy(texture => texture.ParameterIdentity, StringComparer.Ordinal).ToArray(),
            diagnostics,
            resources.Values.OrderBy(resource => resource.StableId, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Uses upstream static-mesh material slots and LOD0 section topology.
    /// </summary>
    public FModelMaterialProjection BuildFromStaticMeshDto(
        string objectPath,
        StaticMeshDto dto,
        ExportOptions options,
        FModelUeFormatPolicy policy,
        FModelTextureExportPreview? texturePreview = null,
        string? ownerModelUri = null)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return BuildFromMeshData(
            objectPath,
            dto.Materials,
            dto.LODs.Count == 0 ? Array.Empty<MeshSectionDto>() : dto.LODs[0].Sections.ToArray(),
            options,
            policy,
            texturePreview,
            ownerModelUri);
    }

    /// <summary>
    /// Uses upstream skeletal-mesh material slots and LOD0 section topology. The
    /// FMODEL_MATERIALS wire payload remains mesh-kind-neutral even though its
    /// historical CLR contract names retain the StaticMesh prefix.
    /// </summary>
    public FModelMaterialProjection BuildFromSkeletalMeshDto(
        string objectPath,
        SkeletalMeshDto dto,
        ExportOptions options,
        FModelUeFormatPolicy policy,
        FModelTextureExportPreview? texturePreview = null,
        string? ownerModelUri = null)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return BuildFromMeshData(
            objectPath,
            dto.Materials,
            dto.LODs.Count == 0 ? Array.Empty<MeshSectionDto>() : dto.LODs[0].Sections.ToArray(),
            options,
            policy,
            texturePreview,
            ownerModelUri);
    }

    /// <summary>
    /// Common projection path for mesh DTOs. It optionally invokes the queue-owned
    /// TextureExporter through a metadata-only preview; it never enqueues or writes
    /// material or texture resources itself.
    /// </summary>
    private FModelMaterialProjection BuildFromMeshData(
        string objectPath,
        IReadOnlyList<MeshMaterialDto> materials,
        IReadOnlyList<MeshSectionDto> referenceSections,
        ExportOptions options,
        FModelUeFormatPolicy policy,
        FModelTextureExportPreview? texturePreview,
        string? ownerModelUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectPath);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(referenceSections);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(policy);

        if (!policy.Enabled || policy.MaterialMode == FModelUeFormatMaterialMode.Disabled)
            return Build(objectPath, materials.Count, Array.Empty<FModelMaterialSlotInput>(), policy, ownerModelUri);

        if (!options.ExportMaterials)
        {
            var severity = policy.RequiresCompleteMaterialProjection
                ? FModelUeFormatDiagnosticSeverity.Error
                : FModelUeFormatDiagnosticSeverity.Warning;
            return new FModelMaterialProjection(
                objectPath,
                Array.Empty<FModelMaterialLinkProjection>(),
                Array.Empty<FModelTextureLinkProjection>(),
                [Diagnostic(objectPath, "material.export-disabled", severity, "Upstream material export is disabled; V3 links were omitted.", "Enable material export")]);
        }

        var slots = new List<FModelMaterialSlotInput>(referenceSections.Count);
        for (var sectionIndex = 0; sectionIndex < referenceSections.Count; sectionIndex++)
        {
            var sourceMaterialIndex = referenceSections[sectionIndex].MaterialIndex;
            var slotName = sourceMaterialIndex >= 0 && sourceMaterialIndex < materials.Count
                ? materials[sourceMaterialIndex].SlotName
                : $"MaterialSlot_{sectionIndex}";
            var source = ResolveSourceMaterial(sourceMaterialIndex, slotName);
            slots.Add(source with
            {
                SlotIndex = sectionIndex,
                SourceMaterialIndex = sourceMaterialIndex
            });
        }

        return Build(objectPath, materials.Count, slots, policy, ownerModelUri);

        FModelMaterialSlotInput ResolveSourceMaterial(int sourceMaterialIndex, string slotName)
        {
            if (sourceMaterialIndex < 0 || sourceMaterialIndex >= materials.Count)
                return new FModelMaterialSlotInput(sourceMaterialIndex, sourceMaterialIndex, slotName, string.Empty, null, Array.Empty<FModelTextureInput>());

            var sourceSlot = materials[sourceMaterialIndex];
            var sourceIdentity = sourceSlot.Material?.ResolvedObject?.GetPathName() ?? string.Empty;
            UMaterialInterface? material = null;
            try
            {
                material = sourceSlot.Material?.Load<UMaterialInterface>();
            }
            catch
            {
                // The pure projection pass reports the missing identity/URI; runtime
                // diagnostics remain redacted and the exporter boundary decides failure.
            }

            if (material is null)
                return new FModelMaterialSlotInput(sourceMaterialIndex, sourceMaterialIndex, slotName, sourceIdentity, null, Array.Empty<FModelTextureInput>());

            var parameters = new CMaterialParams2();
            try
            {
                material.GetParams(parameters, options.MaterialDepth);
            }
            catch
            {
                return new FModelMaterialSlotInput(sourceMaterialIndex, sourceMaterialIndex, slotName, material.GetPathName(), null, Array.Empty<FModelTextureInput>());
            }

            var textureInputs = SelectPrimaryPbrTextures(
                parameters.Textures
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => BuildTextureInput(pair.Key, pair.Value))
                    .ToArray());
            string? materialJsonUri = null;
            try
            {
                materialJsonUri = FModelMaterialUriResolver.ResolveMaterialJsonUri(material, policy.ResourceUriRoot, ownerModelUri);
            }
            catch (ArgumentException)
            {
                // The projection pass records an invalid URI without exposing source paths.
            }

            return new FModelMaterialSlotInput(
                sourceMaterialIndex,
                sourceMaterialIndex,
                slotName,
                material.GetPathName(),
                materialJsonUri,
                textureInputs);

            FModelTextureInput BuildTextureInput(string parameterIdentity, UUnrealMaterial sourceTexture)
            {
                IReadOnlyList<FModelTextureOutputProjection>? outputs = null;
                bool? metadataAvailable = null;
                if (texturePreview is not null && sourceTexture is UTexture texture)
                {
                    metadataAvailable = texturePreview.TryProject(texture, out var projected);
                    outputs = projected;
                }

                string? textureUri = outputs?.FirstOrDefault()?.LogicalResourceUri;
                if (textureUri is null)
                {
                    try
                    {
                        textureUri = FModelMaterialUriResolver.ResolveTextureUri(sourceTexture, options, policy.ResourceUriRoot, ownerModelUri);
                    }
                    catch (ArgumentException)
                    {
                        // The projection pass records an invalid URI without exposing source paths.
                    }
                }

                return new FModelTextureInput(parameterIdentity, sourceTexture.GetPathName(), textureUri)
                {
                    Outputs = outputs,
                    ExportMetadataAvailable = metadataAvailable,
                    MapKind = TryClassifyMap(parameterIdentity, sourceTexture, out var kind) ? kind : null,
                    IsSrgb = sourceTexture is UTexture texture2 && texture2.SRGB,
                    SourceTextureIsNormalMap = sourceTexture is UTexture texture3 && texture3.IsNormalMap
                };
            }
        }
    }

    /// <summary>
    /// CMaterialParams2 exposes both the canonical PM_* fallback entries and a
    /// potentially large set of implementation-specific texture parameters. The
    /// UEFormat wire contract supports one binding per PBR map kind, so emitting
    /// every recognized parameter can create conflicting BaseColor/Normal/Orm
    /// bindings and cause the complete extension to be discarded. Keep one
    /// deterministic primary candidate for each supported map kind, preferring
    /// CMaterialParams2's canonical fallback aliases.
    /// </summary>
    private static IReadOnlyList<FModelTextureInput> SelectPrimaryPbrTextures(
        IReadOnlyList<FModelTextureInput> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var selected = candidates
            .Where(input => input.MapKind is not null)
            .GroupBy(input => input.MapKind!.Value)
            .Select(group => group
                // Prefer a successfully previewed output, but retain one failed
                // candidate when every preview failed. Dropping failed candidates
                // here bypasses Build()'s texture.export.metadata diagnostic and
                // incorrectly lets Strict mode serialize a material-only success.
                .OrderBy(input => input.Outputs is { Count: > 0 } ? 0 : 1)
                .ThenBy(input => TexturePriority(input, group.Key))
                .ThenBy(input => input.ParameterIdentity, StringComparer.Ordinal)
                .ThenBy(input => input.SourceIdentity, StringComparer.Ordinal)
                .First())
            .ToList();

        if (selected.All(input => input.MapKind != PbrMapKind.BaseColor))
        {
            var fallback = candidates
                .Where(input => input.MapKind is null
                    && !LooksClearlyNonBaseColor(input.ParameterIdentity))
                .OrderBy(input => input.Outputs is { Count: > 0 } ? 0 : 1)
                .ThenBy(input => TexturePriority(input, PbrMapKind.BaseColor))
                .ThenBy(input => input.ParameterIdentity, StringComparer.Ordinal)
                .ThenBy(input => input.SourceIdentity, StringComparer.Ordinal)
                .FirstOrDefault();
            if (fallback is not null)
            {
                selected.Add(fallback with
                {
                    MapKind = PbrMapKind.BaseColor,
                    IsSrgb = fallback.IsSrgb ?? true
                });
            }
        }

        return selected
            .OrderBy(input => input.MapKind)
            .ThenBy(input => input.ParameterIdentity, StringComparer.Ordinal)
            .ToArray();
    }

    private static int TexturePriority(FModelTextureInput input, PbrMapKind mapKind)
    {
        var normalized = NormalizeParameter(input.ParameterIdentity ?? string.Empty);
        if (mapKind == PbrMapKind.BaseColor && normalized == "pmdiffuse") return 0;
        if (mapKind == PbrMapKind.Normal && normalized == "pmnormals") return 0;
        if (mapKind == PbrMapKind.Orm && normalized == "pmspecularmasks") return 0;
        if (input.SourceTextureIsNormalMap && mapKind == PbrMapKind.Normal) return 1;
        return 10;
    }

    private static bool LooksClearlyNonBaseColor(string? parameterIdentity)
    {
        var normalized = NormalizeParameter(parameterIdentity ?? string.Empty);
        return normalized.Contains("emiss", StringComparison.Ordinal)
            || normalized.Contains("opacity", StringComparison.Ordinal)
            || normalized.Contains("alpha", StringComparison.Ordinal)
            || normalized.Contains("height", StringComparison.Ordinal)
            || normalized.Contains("displacement", StringComparison.Ordinal)
            || normalized.Contains("detail", StringComparison.Ordinal)
            || normalized.Contains("mask", StringComparison.Ordinal)
            || normalized.Contains("rough", StringComparison.Ordinal)
            || normalized.Contains("metal", StringComparison.Ordinal)
            || normalized.Contains("occl", StringComparison.Ordinal)
            || normalized.Contains("ambient", StringComparison.Ordinal);
    }
    private static bool HasSameResourceMetadata(
        FModelTextureResourceProjection left,
        FModelTextureResourceProjection right) =>
        string.Equals(left.StableId, right.StableId, StringComparison.Ordinal)
        && string.Equals(left.SourceIdentity, right.SourceIdentity, StringComparison.Ordinal)
        && string.Equals(left.LogicalResourceUri, right.LogicalResourceUri, StringComparison.Ordinal)
        && left.ByteLength == right.ByteLength
        && left.IsEmbedded == right.IsEmbedded
        && left.LocationMode == right.LocationMode
        && left.Sha256.SequenceEqual(right.Sha256);

    private static bool TryClassifyMap(string? parameterIdentity, UUnrealMaterial sourceTexture, out PbrMapKind mapKind)
    {
        if (sourceTexture is UTexture texture && texture.IsNormalMap)
        {
            mapKind = PbrMapKind.Normal;
            return true;
        }
        return TryClassifyMap(parameterIdentity, out mapKind);
    }

    private static bool TryClassifyMap(FModelTextureInput texture, out PbrMapKind mapKind)
    {
        if (texture.MapKind is { } explicitKind)
        {
            mapKind = explicitKind;
            return true;
        }
        if (texture.SourceTextureIsNormalMap)
        {
            mapKind = PbrMapKind.Normal;
            return true;
        }
        return TryClassifyMap(texture.ParameterIdentity, out mapKind);
    }

    private static bool TryClassifyMap(string? parameterIdentity, out PbrMapKind mapKind)
    {
        mapKind = default;
        if (string.IsNullOrWhiteSpace(parameterIdentity))
            return false;
        var normalized = NormalizeParameter(parameterIdentity);
        if (normalized.Contains("normal", StringComparison.Ordinal) || normalized is "n" or "nm" or "nrm" or "pmnormals")
        {
            mapKind = PbrMapKind.Normal;
            return true;
        }
        if (normalized.Contains("orm", StringComparison.Ordinal)
            || normalized.Contains("mra", StringComparison.Ordinal)
            || normalized.Contains("mro", StringComparison.Ordinal)
            || normalized.Contains("rma", StringComparison.Ordinal)
            || normalized.Contains("metalrough", StringComparison.Ordinal)
            || normalized.Contains("roughnessocclusion", StringComparison.Ordinal)
            || normalized is "pmspecularmasks" or "pak" or "arm")
        {
            mapKind = PbrMapKind.Orm;
            return true;
        }
        if (normalized.Contains("diff", StringComparison.Ordinal)
            || normalized.Contains("albedo", StringComparison.Ordinal)
            || normalized.Contains("basecolor", StringComparison.Ordinal)
            || normalized.Contains("basecolour", StringComparison.Ordinal)
            || normalized.Contains("color", StringComparison.Ordinal)
            || normalized is "pmdiffuse")
        {
            mapKind = PbrMapKind.BaseColor;
            return true;
        }
        return false;
    }
    private static string NormalizeParameter(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static bool TryNormalizeUri(string? value, out string? normalized, bool allowLeadingParents)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        try
        {
            normalized = FModelMaterialUriResolver.NormalizeResourceUri(value, allowLeadingParents);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void AddInvalid(
        ICollection<FModelUeFormatPipelineDiagnostic> diagnostics,
        string objectPath,
        FModelUeFormatPolicy policy,
        string code,
        string message,
        string? subject)
    {
        diagnostics.Add(Diagnostic(
            objectPath,
            code,
            policy.RequiresCompleteMaterialProjection ? FModelUeFormatDiagnosticSeverity.Error : FModelUeFormatDiagnosticSeverity.Warning,
            message,
            subject));
    }

    private static FModelUeFormatPipelineDiagnostic Diagnostic(
        string objectPath,
        string code,
        FModelUeFormatDiagnosticSeverity severity,
        string message,
        string? subject) => new(
            code,
            severity,
            FModelUeFormatPipelineStage.MaterialProjection,
            objectPath,
            message,
            materialSlot: subject,
            evidenceGeneration: "fmodel-export-pipeline-v2/v3-adapter-v1");
}

public sealed record FModelMaterialSlotInput(
    int SlotIndex,
    int SourceMaterialIndex,
    string SlotName,
    string? SourceIdentity,
    string? MaterialJsonUri,
    IReadOnlyList<FModelTextureInput>? Textures = null);

public sealed record FModelTextureInput(
    string? ParameterIdentity,
    string? SourceIdentity,
    string? TextureUri)
{
    public IReadOnlyList<FModelTextureOutputProjection>? Outputs { get; init; }
    public bool? ExportMetadataAvailable { get; init; }
    public PbrMapKind? MapKind { get; init; }
    public bool? IsSrgb { get; init; }
    public bool SourceTextureIsNormalMap { get; init; }
}
