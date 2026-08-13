using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using CUE4Parse.UeFormat.Protocol;

namespace CUE4Parse.UeFormat.UEModel;

public static class FModelTextureChunkWriter
{
    private const string ChunkName = "FMODEL_TEXTURES";
    private const byte Version = 1;
    private const byte VersionWithLocation = 2;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static UeFormatExtensionChunk WriteV1(TextureResourceSet set) => Write(set, Version);

    public static UeFormatExtensionChunk WriteV2(TextureResourceSet set) => Write(set, VersionWithLocation);

    private static UeFormatExtensionChunk Write(TextureResourceSet set, byte version)
    {
        ArgumentNullException.ThrowIfNull(set);

        ValidateTextures(set.Textures);
        ValidateBindings(set.Textures, set.Bindings);

        var payload = new List<byte> { version };
        WriteInt32(payload, set.Textures.Length);
        foreach (var texture in set.Textures)
        {
            WriteString(payload, texture.StableId);
            WriteString(payload, texture.LogicalResourceUri);
            if (version >= VersionWithLocation)
                payload.Add((byte)texture.LocationMode);
            payload.Add(texture.IsEmbedded ? (byte)1 : (byte)0);
            WriteInt32(payload, texture.ByteLength);
            payload.AddRange(texture.Sha256.ToArray());
        }

        WriteInt32(payload, set.Bindings.Length);
        foreach (var binding in set.Bindings)
        {
            WriteString(payload, binding.MaterialId);
            payload.Add((byte)binding.MapKind);
            WriteString(payload, binding.TextureResourceId);
            payload.Add(binding.IsSrgb ? (byte)1 : (byte)0);
        }

        return new UeFormatExtensionChunk(ChunkName, 1, payload.ToArray());
    }

    private static void ValidateTextures(ImmutableArray<TextureResource> textures)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? previousStableId = null;
        foreach (var texture in textures)
        {
            if (!seen.Add(texture.StableId))
                throw new ArgumentException("Texture stable IDs must be unique.", nameof(textures));
            if (previousStableId is not null && string.CompareOrdinal(previousStableId, texture.StableId) >= 0)
                throw new ArgumentException("Textures must be in canonical ascending StableId order.", nameof(textures));
            previousStableId = texture.StableId;
        }
    }

    private static void ValidateBindings(ImmutableArray<TextureResource> textures, ImmutableArray<PbrTextureBinding> bindings)
    {
        var textureIds = textures.Select(texture => texture.StableId).ToHashSet(StringComparer.Ordinal);
        string? previousMaterialId = null;
        PbrMapKind? previousMapKind = null;
        var seenPairs = new HashSet<(string MaterialId, PbrMapKind MapKind)>();

        foreach (var binding in bindings)
        {
            if (!textureIds.Contains(binding.TextureResourceId))
                throw new ArgumentException("Bindings must reference an existing texture stable ID.", nameof(bindings));
            if (!seenPairs.Add((binding.MaterialId, binding.MapKind)))
                throw new ArgumentException("Each material can bind each PBR map kind at most once.", nameof(bindings));

            if (previousMaterialId is not null)
            {
                var materialOrder = string.CompareOrdinal(previousMaterialId, binding.MaterialId);
                if (materialOrder > 0)
                    throw new ArgumentException("Bindings must be in canonical MaterialId/MapKind order.", nameof(bindings));
                if (materialOrder == 0 && previousMapKind is not null && previousMapKind.Value >= binding.MapKind)
                    throw new ArgumentException("Bindings must be in canonical MaterialId/MapKind order.", nameof(bindings));
            }

            previousMaterialId = binding.MaterialId;
            previousMapKind = binding.MapKind;
        }
    }

    private static void WriteInt32(List<byte> destination, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        destination.AddRange(buffer.ToArray());
    }

    private static void WriteString(List<byte> destination, string value)
    {
        var encoded = StrictUtf8.GetBytes(value);
        WriteInt32(destination, encoded.Length);
        destination.AddRange(encoded);
    }
}
