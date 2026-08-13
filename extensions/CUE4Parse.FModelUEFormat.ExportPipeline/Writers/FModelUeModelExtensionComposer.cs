using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using CUE4Parse.FModelUEFormat.ExportPipeline.Materials;
using CUE4Parse.UeFormat.Protocol;
using CUE4Parse.UeFormat.UEModel;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.Writers;

/// <summary>
/// Appends V3 extension chunks to an upstream UEMODEL logical payload.
/// If the upstream file is compressed, the envelope is decompressed and rebuilt while
/// preserving its compression format; the standard chunk bytes are otherwise unchanged.
/// </summary>
public sealed class FModelUeModelExtensionComposer
{
    public byte[] AppendMaterialLinks(ReadOnlyMemory<byte> standardBytes, FModelMaterialProjection projection, int sourceMaterialCount)
        => AppendProjection(standardBytes, projection, sourceMaterialCount);

    public byte[] AppendProjection(ReadOnlyMemory<byte> standardBytes, FModelMaterialProjection projection, int sourceMaterialCount)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (standardBytes.IsEmpty)
            throw new ArgumentException("Standard UEMODEL bytes are required.", nameof(standardBytes));
        if (sourceMaterialCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceMaterialCount));
        if (!projection.IsComplete)
            throw new InvalidOperationException("Material projection contains errors.");
        if (projection.Materials.Count == 0)
            throw new InvalidOperationException("Material projection contains no standard material slots.");

        var links = projection.Materials
            .OrderBy(material => material.SlotIndex)
            .Select(material => new StaticMeshMaterialLink(
                material.SlotIndex,
                material.SourceMaterialIndex,
                material.SlotName,
                material.SourceIdentity,
                material.MaterialJsonUri))
            .ToImmutableArray();

        var materialExtension = FModelMaterialLinkChunkWriter.WriteV2(new StaticMeshMaterialLinkSet(sourceMaterialCount, links));
        var extensionBytes = new List<byte>(materialExtension.Length + 256);
        extensionBytes.AddRange(materialExtension);

        if (projection.Textures.Count > 0)
        {
            var textureSet = BuildTextureResourceSet(projection);
            var textureExtension = textureSet.Textures.Any(texture => texture.LocationMode != FModelTextureResourceLocation.ExportRoot)
                ? FModelTextureChunkWriter.WriteV2(textureSet)
                : FModelTextureChunkWriter.WriteV1(textureSet);
            extensionBytes.AddRange(SerializeChunk(textureExtension));
        }

        // UEFormat compression wraps the complete chunk stream, not just individual
        // chunks. Append to the logical payload and rebuild the original envelope so
        // ZSTD/GZIP files do not silently fall back to vanilla UEMODEL output.
        var output = UeModelCompression.AppendPayload(standardBytes, extensionBytes.ToArray());

        // This is an independent final-byte validation. It is intentionally after the
        // append so the standard MATERIALS-to-extension cross-links are checked.
        _ = UeModelInspector.Inspect(UeModelCompression.ToUncompressed(output));
        return output;
    }

    private static TextureResourceSet BuildTextureResourceSet(FModelMaterialProjection projection)
    {
        var resources = projection.TextureResources
            .OrderBy(resource => resource.StableId, StringComparer.Ordinal)
            .Select(resource => new TextureResource(
                resource.StableId,
                resource.LogicalResourceUri,
                resource.ByteLength,
                resource.Sha256.ToImmutableArray(),
                resource.IsEmbedded,
                resource.LocationMode))
            .ToImmutableArray();

        if (resources.Length == 0)
            throw new InvalidOperationException("Texture bindings exist but no texture resources were projected.");

        var materialNames = projection.Materials.ToDictionary(material => material.SlotIndex, material => material.SlotName);
        var bindings = new List<PbrTextureBinding>();
        var bindingByMaterialAndMap = new Dictionary<(string MaterialId, PbrMapKind MapKind), PbrTextureBinding>();
        foreach (var texture in projection.Textures
                     .OrderBy(texture => texture.MaterialId ?? materialNames.GetValueOrDefault(texture.MaterialSlotIndex) ?? string.Empty, StringComparer.Ordinal)
                     .ThenBy(texture => texture.MapKind ?? PbrMapKind.BaseColor)
                     .ThenBy(texture => texture.TextureResourceId, StringComparer.Ordinal))
        {
            if (texture.MapKind is not { } mapKind)
                throw new InvalidOperationException($"Texture parameter '{texture.ParameterIdentity}' has no PBR map kind.");
            if (string.IsNullOrWhiteSpace(texture.TextureResourceId))
                throw new InvalidOperationException($"Texture parameter '{texture.ParameterIdentity}' has no resource ID.");
            if (!materialNames.TryGetValue(texture.MaterialSlotIndex, out var slotName))
                throw new InvalidOperationException($"Texture parameter '{texture.ParameterIdentity}' references an unknown material slot.");

            var materialId = string.IsNullOrWhiteSpace(texture.MaterialId) ? slotName : texture.MaterialId;
            var binding = new PbrTextureBinding(materialId, mapKind, texture.TextureResourceId, texture.IsSrgb);
            var key = (binding.MaterialId, binding.MapKind);
            if (bindingByMaterialAndMap.TryGetValue(key, out var existing))
            {
                if (!existing.Equals(binding))
                    throw new InvalidOperationException($"Material '{binding.MaterialId}' has conflicting {binding.MapKind} texture bindings.");
                continue;
            }

            bindingByMaterialAndMap.Add(key, binding);
            bindings.Add(binding);
        }

        return new TextureResourceSet(resources, bindings.ToImmutableArray());
    }

    private static byte[] SerializeChunk(UeFormatExtensionChunk chunk)
    {
        var bytes = new List<byte>();
        WriteString(bytes, chunk.Id);
        WriteInt32(bytes, chunk.Count);
        var payload = chunk.Payload.ToArray();
        WriteInt32(bytes, payload.Length);
        bytes.AddRange(payload);
        return bytes.ToArray();
    }

    private static void WriteInt32(List<byte> destination, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        destination.AddRange(buffer.ToArray());
    }

    private static void WriteString(List<byte> destination, string value)
    {
        var encoded = new UTF8Encoding(false, true).GetBytes(value);
        WriteInt32(destination, encoded.Length);
        destination.AddRange(encoded);
    }
}
