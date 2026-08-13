using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Text;

namespace CUE4Parse.UeFormat.Protocol;

public static class SkeletonIdentityInspector
{
    public static SkeletonIdentityV1 ReadModelIdentity(UeFormatExtensionChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ValidateChunkHeader(chunk, expectedId: "FMODEL_SKELETON_IDENTITY");

        using var stream = new MemoryStream(chunk.Payload.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        var identity = ReadIdentity(reader, requireExactRemainingHashLength: true);
        EnsureFullyConsumed(stream);
        return identity;
    }

    public static (string SourceObjectPath, SkeletonIdentityV1 Identity) ReadTargetIdentity(UeFormatExtensionChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ValidateChunkHeader(chunk, expectedId: "FMODEL_TARGET_SKELETON");

        using var stream = new MemoryStream(chunk.Payload.ToArray(), writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        var version = ReadRequiredByte(reader, "version");
        if (version != SkeletonIdentityV1.Version) throw new ArgumentException($"Unsupported skeleton identity version: {version}.", nameof(chunk));

        var sourceObjectPath = ReadLogicalPath(reader, "sourceObjectPath");
        var identity = ReadIdentityAfterVersion(reader, requireExactRemainingHashLength: false);
        EnsureFullyConsumed(stream);
        return (sourceObjectPath, identity);
    }

    private static SkeletonIdentityV1 ReadIdentity(BinaryReader reader, bool requireExactRemainingHashLength)
    {
        var version = ReadRequiredByte(reader, "version");
        if (version != SkeletonIdentityV1.Version) throw new ArgumentException($"Unsupported skeleton identity version: {version}.");
        return ReadIdentityAfterVersion(reader, requireExactRemainingHashLength);
    }

    private static SkeletonIdentityV1 ReadIdentityAfterVersion(BinaryReader reader, bool requireExactRemainingHashLength)
    {
        var skeletonPath = ReadLogicalPath(reader, "skeletonPath");
        var guid = ReadOptionalGuid(reader);
        var remaining = reader.BaseStream.Length - reader.BaseStream.Position;
        if (remaining < 32 || (requireExactRemainingHashLength && remaining != 32))
            throw new ArgumentException($"Invalid boneLayoutHash length: expected 32 bytes but found {remaining}.");
        var hash = ReadExactBytes(reader, 32, "boneLayoutHash");
        return new SkeletonIdentityV1(skeletonPath, guid, ImmutableArray.CreateRange(hash));
    }

    private static void ValidateChunkHeader(UeFormatExtensionChunk chunk, string expectedId)
    {
        if (!string.Equals(chunk.Id, expectedId, StringComparison.Ordinal))
            throw new ArgumentException($"Expected {expectedId} chunk id but received {chunk.Id}.", nameof(chunk));
        if (chunk.Count != 1)
            throw new ArgumentException($"Chunk Count must be exactly 1 for {expectedId}.", nameof(chunk));
    }

    private static string ReadLogicalPath(BinaryReader reader, string fieldName)
    {
        var value = ReadFString(reader, fieldName);
        SkeletonIdentityChunkWriter.ValidateLogicalUnrealPath(value, fieldName);
        return value;
    }

    private static string ReadFString(BinaryReader reader, string fieldName)
    {
        var byteLength = ReadInt32(reader, fieldName);
        if (byteLength <= 0) throw new ArgumentException($"Invalid {fieldName} byte length: {byteLength}.");

        var bytes = ReadExactBytes(reader, byteLength, fieldName);
        if (bytes[^1] != 0) throw new ArgumentException($"Invalid {fieldName}: expected NUL-terminated UTF-8 string.");

        string value;
        try
        {
            value = new UTF8Encoding(false, true).GetString(bytes, 0, bytes.Length - 1);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ArgumentException($"Invalid {fieldName}: invalid UTF-8 string.", fieldName, ex);
        }
        if (value.Length == 0) throw new ArgumentException($"Invalid {fieldName}: value must be non-empty.");
        if (value.Contains('\0')) throw new ArgumentException($"Invalid {fieldName}: embedded NUL is not allowed.");
        return value;
    }

    private static Guid? ReadOptionalGuid(BinaryReader reader)
    {
        var marker = ReadRequiredByte(reader, "hasSkeletonGuid");
        return marker switch
        {
            0 => null,
            1 => new Guid(ReadExactBytes(reader, 16, "skeletonGuid")),
            _ => throw new ArgumentException($"Invalid skeleton GUID marker: {marker}.")
        };
    }

    private static int ReadInt32(BinaryReader reader, string fieldName)
    {
        var bytes = ReadExactBytes(reader, 4, fieldName);
        // Skeleton identity payload integers are part of the wire format and are always little-endian.
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    private static byte ReadRequiredByte(BinaryReader reader, string fieldName)
    {
        return ReadExactBytes(reader, 1, fieldName)[0];
    }

    private static byte[] ReadExactBytes(BinaryReader reader, int count, string fieldName)
    {
        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count) throw new ArgumentException($"Invalid {fieldName}: available data ended before reading {count} bytes.");
        return bytes;
    }

    private static void EnsureFullyConsumed(MemoryStream stream)
    {
        if (stream.Position != stream.Length) throw new ArgumentException("Trailing skeleton identity data detected.");
    }
}
