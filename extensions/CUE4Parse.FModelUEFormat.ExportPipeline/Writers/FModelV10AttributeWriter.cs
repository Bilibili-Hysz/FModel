using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using CUE4Parse.FModelUEFormat.ExportPipeline.Materials;
using CUE4Parse.UeFormat.Protocol;
using CUE4Parse.UeFormat.UEModel;
using CUE4Parse_Conversion.Writers.UEFormat.Structs;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.Writers;

/// <summary>
/// Serializes the frozen FModel semantic payloads inside UEFormat V10 root
/// attributes. V10 owns the root envelope, so no bytes are appended after the
/// standard writer has completed and compressed files require no rewrite.
/// </summary>
public sealed class FModelV10AttributeWriter
{
    private const byte EnvelopeVersion = 1;

    public void WriteProjection(
        FDataAttributeSet root,
        FModelMaterialProjection projection,
        int sourceMaterialCount)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(projection);
        if (sourceMaterialCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceMaterialCount));
        if (!projection.IsComplete || projection.Materials.Count == 0)
            throw new InvalidOperationException("A complete material projection is required.");

        var links = projection.Materials
            .OrderBy(material => material.SlotIndex)
            .Select(material => new StaticMeshMaterialLink(
                material.SlotIndex,
                material.SourceMaterialIndex,
                material.SlotName,
                material.SourceIdentity,
                material.MaterialJsonUri))
            .ToImmutableArray();
        var materialChunk = ParseLegacyChunk(
            FModelMaterialLinkChunkWriter.WriteV2(
                new StaticMeshMaterialLinkSet(sourceMaterialCount, links)));
        // Build every chunk before mutating the caller's root. This keeps
        // validation/serialization failures atomic for Linked mode; the
        // caller can then preserve a standard-only UEFormat file.
        UeFormatExtensionChunk? textureChunk = null;
        if (projection.Textures.Count > 0)
        {
            var textureSet = BuildTextureResourceSet(projection);
            textureChunk = textureSet.Textures.Any(
                    texture => texture.LocationMode != FModelTextureResourceLocation.ExportRoot)
                ? FModelTextureChunkWriter.WriteV2(textureSet)
                : FModelTextureChunkWriter.WriteV1(textureSet);
        }

        WriteAttribute(root, materialChunk);
        if (textureChunk is not null)
            WriteAttribute(root, textureChunk);
    }

    private static void WriteAttribute(FDataAttributeSet root, UeFormatExtensionChunk chunk)
    {
        root.AddAttribute(chunk.Id, attribute =>
        {
            attribute.Write(EnvelopeVersion);
            attribute.Write(chunk.Count);
            attribute.Write(chunk.Payload.ToArray());
        });
    }

    private static TextureResourceSet BuildTextureResourceSet(FModelMaterialProjection projection)
    {
        ValidateTextureBindingMaterialIds(projection);

        var resources = projection.TextureResources
            .OrderBy(resource => resource.StableId, StringComparer.Ordinal)
            .Select(resource =>
            {
                if (resource.IsEmbedded)
                    throw new InvalidOperationException(
                        $"V10 FMODEL_TEXTURES identity resource '{resource.StableId}' is marked embedded, " +
                        "but the V10 attribute carries identity metadata only and no texture bytes.");
                return new TextureResource(
                    resource.StableId,
                    resource.LogicalResourceUri,
                    resource.ByteLength,
                    resource.Sha256.ToImmutableArray(),
                    resource.IsEmbedded,
                    resource.LocationMode);
            })
            .ToImmutableArray();
        if (resources.Length == 0)
            throw new InvalidOperationException("Texture bindings exist but no texture resources were projected.");

        var materialNames = projection.Materials.ToDictionary(
            material => material.SlotIndex,
            material => material.SlotName);
        var bindingByMaterialAndMap = new Dictionary<
            (string MaterialId, PbrMapKind MapKind),
            PbrTextureBinding>();
        foreach (var texture in projection.Textures
                     .OrderBy(texture => texture.MaterialId
                                         ?? materialNames.GetValueOrDefault(texture.MaterialSlotIndex)
                                         ?? string.Empty, StringComparer.Ordinal)
                     .ThenBy(texture => texture.MapKind ?? PbrMapKind.BaseColor)
                     .ThenBy(texture => texture.TextureResourceId, StringComparer.Ordinal))
        {
            if (texture.MapKind is not { } mapKind)
                throw new InvalidOperationException(
                    $"Texture parameter '{texture.ParameterIdentity}' has no PBR map kind.");
            if (string.IsNullOrWhiteSpace(texture.TextureResourceId))
                throw new InvalidOperationException(
                    $"Texture parameter '{texture.ParameterIdentity}' has no resource ID.");
            if (!materialNames.TryGetValue(texture.MaterialSlotIndex, out var slotName))
                throw new InvalidOperationException(
                    $"Texture parameter '{texture.ParameterIdentity}' references an unknown material slot.");

            var materialId = string.IsNullOrWhiteSpace(texture.MaterialId)
                ? slotName
                : texture.MaterialId;
            var binding = new PbrTextureBinding(
                materialId,
                mapKind,
                texture.TextureResourceId,
                texture.IsSrgb);
            var key = (binding.MaterialId, binding.MapKind);
            if (bindingByMaterialAndMap.TryGetValue(key, out var existing))
            {
                if (!existing.Equals(binding))
                    throw new InvalidOperationException(
                        $"Material '{binding.MaterialId}' has conflicting {binding.MapKind} texture bindings.");
                continue;
            }

            bindingByMaterialAndMap.Add(key, binding);
        }

        return new TextureResourceSet(
            resources,
            bindingByMaterialAndMap.Values
                .OrderBy(binding => binding.MaterialId, StringComparer.Ordinal)
                .ThenBy(binding => binding.MapKind)
                .ToImmutableArray());
    }

    private static void ValidateTextureBindingMaterialIds(FModelMaterialProjection projection)
    {
        var ambiguousSlotNames = projection.Materials
            .GroupBy(material => material.SlotName, StringComparer.Ordinal)
            .Where(group => group
                .Select(material => material.SourceIdentity)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any())
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (ambiguousSlotNames.Length > 0)
        {
            throw new InvalidOperationException(
                "V10 texture bindings use slot names as MaterialId, but these slot names resolve to multiple Unreal material identities: " +
                string.Join(", ", ambiguousSlotNames));
        }
    }

    private static UeFormatExtensionChunk ParseLegacyChunk(byte[] bytes)
    {
        var offset = 0;
        var id = ReadString(bytes, ref offset);
        var count = ReadInt32(bytes, ref offset);
        var payloadLength = ReadInt32(bytes, ref offset);
        if (payloadLength < 0 || offset + payloadLength != bytes.Length)
            throw new InvalidDataException("FModel extension chunk envelope was malformed.");
        return new UeFormatExtensionChunk(
            id,
            count,
            bytes.AsMemory(offset, payloadLength));
    }

    private static int ReadInt32(byte[] bytes, ref int offset)
    {
        if (offset > bytes.Length - sizeof(int))
            throw new InvalidDataException("FModel extension chunk was truncated.");
        var value = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)));
        offset += sizeof(int);
        return value;
    }

    private static string ReadString(byte[] bytes, ref int offset)
    {
        var length = ReadInt32(bytes, ref offset);
        if (length <= 0 || offset + length > bytes.Length)
            throw new InvalidDataException("FModel extension chunk string was malformed.");
        var value = new UTF8Encoding(false, true).GetString(bytes, offset, length);
        offset += length;
        return value;
    }
}
