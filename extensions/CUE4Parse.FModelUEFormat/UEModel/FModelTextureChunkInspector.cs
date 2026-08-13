using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace CUE4Parse.UeFormat.UEModel;

public static class FModelTextureChunkInspector
{
    public static InspectedTextureChunk Inspect(ReadOnlyMemory<byte> payload) => Inspect(payload.Span);

    public static InspectedTextureChunk Inspect(ReadOnlySpan<byte> payload)
    {
        var reader = new Reader(payload);
        var version = reader.ReadByte("FMODEL_TEXTURES version");
        if (version is not (1 or 2)) throw Invalid($"Unsupported FMODEL_TEXTURES version: {version}.");

        var textureCount = reader.ReadCount("FMODEL_TEXTURES texture count");
        var textures = ImmutableArray.CreateBuilder<InspectedTextureResource>(textureCount);
        var seenTextureIds = new HashSet<string>(StringComparer.Ordinal);
        string? previousStableId = null;

        for (var index = 0; index < textureCount; index++)
        {
            var stableId = reader.ReadString("texture stable ID");
            var logicalResourceUri = reader.ReadString("texture logical resource URI");
            var locationMode = version >= 2
                ? ReadLocationMode(reader.ReadByte("texture location mode"))
                : FModelTextureResourceLocation.ExportRoot;
            var isEmbedded = reader.ReadBoolean("texture embedded flag");
            if (version >= 2 && isEmbedded && locationMode != FModelTextureResourceLocation.Embedded)
                throw Invalid("Embedded textures must use Embedded location mode.");
            var byteLength = reader.ReadInt32("texture byte length");
            var sha256 = reader.ReadExactHash("texture SHA-256 hash");

            if (MaterialLinkSemanticValidation.IsBlankOrContainsNul(stableId))
                throw Invalid("Texture StableId must not be blank.");
            if (!MaterialLinkSemanticValidation.IsLogicalUri(logicalResourceUri, locationMode == FModelTextureResourceLocation.Relative))
                throw Invalid("Texture LogicalResourceUri must be a non-rooted logical URI.");
            if (byteLength < 0)
                throw Invalid("Texture ByteLength must be non-negative.");
            if (!seenTextureIds.Add(stableId))
                throw Invalid("Texture StableId values must be unique.");
            if (previousStableId is not null && string.CompareOrdinal(previousStableId, stableId) >= 0)
                throw Invalid("Textures must be in canonical ascending StableId order.");

            previousStableId = stableId;
            textures.Add(new InspectedTextureResource(stableId, logicalResourceUri, isEmbedded, byteLength, sha256, locationMode));
        }

        var bindingCount = reader.ReadCount("FMODEL_TEXTURES binding count");
        var bindings = ImmutableArray.CreateBuilder<InspectedPbrTextureBinding>(bindingCount);
        var seenPairs = new HashSet<(string MaterialId, PbrMapKind MapKind)>();
        var knownTextureIds = textures.Select(static texture => texture.StableId).ToHashSet(StringComparer.Ordinal);
        string? previousMaterialId = null;
        PbrMapKind? previousMapKind = null;

        for (var index = 0; index < bindingCount; index++)
        {
            var materialId = reader.ReadString("binding material ID");
            var mapKindValue = reader.ReadByte("binding MapKind");
            if (!Enum.IsDefined(typeof(PbrMapKind), mapKindValue))
                throw Invalid("Binding MapKind must be BaseColor, Normal, or Orm.");
            var mapKind = (PbrMapKind)mapKindValue;
            var textureResourceId = reader.ReadString("binding texture resource ID");
            var isSrgb = reader.ReadBoolean("binding sRGB flag");

            if (MaterialLinkSemanticValidation.IsBlankOrContainsNul(materialId))
                throw Invalid("Binding MaterialId must not be blank.");
            if (MaterialLinkSemanticValidation.IsBlankOrContainsNul(textureResourceId))
                throw Invalid("Binding TextureResourceId must not be blank.");
            if (!knownTextureIds.Contains(textureResourceId))
                throw Invalid("Binding texture reference must point to an existing texture StableId.");
            if (!seenPairs.Add((materialId, mapKind)))
                throw Invalid("Each material can bind each PBR map kind at most once.");

            if (previousMaterialId is not null)
            {
                var materialOrder = string.CompareOrdinal(previousMaterialId, materialId);
                if (materialOrder > 0)
                    throw Invalid("Bindings must be in canonical MaterialId/MapKind order.");
                if (materialOrder == 0 && previousMapKind is not null && previousMapKind.Value >= mapKind)
                    throw Invalid("Bindings must be in canonical MaterialId/MapKind order.");
            }

            previousMaterialId = materialId;
            previousMapKind = mapKind;
            bindings.Add(new InspectedPbrTextureBinding(materialId, mapKind, textureResourceId, isSrgb));
        }

        if (!reader.End) throw Invalid("Unread trailing bytes in FMODEL_TEXTURES chunk.");

        return new(version, textures.ToImmutable(), bindings.ToImmutable());
    }

    private static FModelTextureResourceLocation ReadLocationMode(byte value) => value switch
    {
        (byte)FModelTextureResourceLocation.Relative => FModelTextureResourceLocation.Relative,
        (byte)FModelTextureResourceLocation.Bundle => FModelTextureResourceLocation.Bundle,
        (byte)FModelTextureResourceLocation.Embedded => FModelTextureResourceLocation.Embedded,
        (byte)FModelTextureResourceLocation.ExportRoot => FModelTextureResourceLocation.ExportRoot,
        _ => throw Invalid("Texture location mode is unsupported.")
    };

    private static InvalidDataException Invalid(string message) => new(message);

    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private ReadOnlySpan<byte> _remaining = bytes;

        public bool End => _remaining.IsEmpty;

        public byte ReadByte(string description) => ReadBytes(1, description)[0];

        public bool ReadBoolean(string description) => ReadByte(description) switch
        {
            0 => false,
            1 => true,
            _ => throw Invalid($"Invalid {description}.")
        };

        public int ReadInt32(string description) => BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(sizeof(int), description));

        public int ReadCount(string description)
        {
            var value = ReadInt32(description);
            if (value < 0 || value > _remaining.Length) throw Invalid($"Invalid {description}.");
            return value;
        }

        public string ReadString(string description)
        {
            var length = ReadCount($"{description} length");
            var value = ReadBytes(length, description);
            try
            {
                var text = StrictUtf8.GetString(value);
                if (text.IndexOf('\0') >= 0) throw Invalid($"Invalid {description} NUL character.");
                return text;
            }
            catch (DecoderFallbackException)
            {
                throw Invalid($"Invalid {description} UTF-8.");
            }
        }

        public ImmutableArray<byte> ReadExactHash(string description) => ImmutableArray.Create(ReadBytes(32, description).ToArray());

        public ReadOnlySpan<byte> ReadBytes(int length, string description)
        {
            if (length < 0 || length > _remaining.Length) throw Invalid($"{description} exceeds remaining input.");
            var result = _remaining[..length];
            _remaining = _remaining[length..];
            return result;
        }
    }
}
