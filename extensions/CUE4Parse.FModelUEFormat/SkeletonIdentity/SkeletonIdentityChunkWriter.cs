using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace CUE4Parse.UeFormat.Protocol;

public static class SkeletonIdentityChunkWriter
{
    private static readonly Regex DriveAbsolutePathPattern = new("^[A-Za-z]:[/\\\\]", RegexOptions.CultureInvariant);
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static UeFormatExtensionChunk WriteModelIdentity(SkeletonIdentityV1 identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ValidateLogicalUnrealPath(identity.SkeletonPath, nameof(identity.SkeletonPath));
        var boneLayoutHash = CopyAndValidateHash(identity.BoneLayoutHash, nameof(identity.BoneLayoutHash));

        using var payload = new MemoryStream();
        WriteIdentityPayload(payload, identity.SkeletonPath, identity.SkeletonGuid, boneLayoutHash);
        return new UeFormatExtensionChunk("FMODEL_SKELETON_IDENTITY", 1, payload.ToArray());
    }

    public static UeFormatExtensionChunk WriteTargetIdentity(string sourceObjectPath, SkeletonIdentityV1 identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ValidateLogicalUnrealPath(sourceObjectPath, nameof(sourceObjectPath));
        ValidateLogicalUnrealPath(identity.SkeletonPath, nameof(identity.SkeletonPath));
        var boneLayoutHash = CopyAndValidateHash(identity.BoneLayoutHash, nameof(identity.BoneLayoutHash));

        using var payload = new MemoryStream();
        payload.WriteByte(SkeletonIdentityV1.Version);
        WriteFString(payload, sourceObjectPath, nameof(sourceObjectPath));
        WriteFString(payload, identity.SkeletonPath, nameof(identity.SkeletonPath));
        WriteOptionalGuid(payload, identity.SkeletonGuid);
        payload.Write(boneLayoutHash, 0, boneLayoutHash.Length);
        return new UeFormatExtensionChunk("FMODEL_TARGET_SKELETON", 1, payload.ToArray());
    }

    internal static void ValidateLogicalUnrealPath(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Logical Unreal path must be non-empty.", paramName);
        if (!string.Equals(path, path.Trim(), StringComparison.Ordinal)) throw new ArgumentException("Logical Unreal path must not include leading or trailing whitespace.", paramName);
        if (path.Contains('\0')) throw new ArgumentException("Logical Unreal path must not contain NUL.", paramName);
        if (path.Contains('\\')) throw new ArgumentException("Logical Unreal path must use forward slashes only.", paramName);
        if (DriveAbsolutePathPattern.IsMatch(path) || path.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("//", StringComparison.Ordinal) || path.StartsWith("\\\\", StringComparison.Ordinal))
            throw new ArgumentException("Logical Unreal path must not be an absolute filesystem path.", paramName);
        if (!path.StartsWith("/", StringComparison.Ordinal)) throw new ArgumentException("Logical Unreal path must be rooted at '/'.", paramName);

        var segments = path.Split('/', StringSplitOptions.None);
        if (segments.Length < 2) throw new ArgumentException("Logical Unreal path must contain at least one non-root segment.", paramName);
        foreach (var segment in segments.Skip(1))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
                throw new ArgumentException("Logical Unreal path must not contain empty or traversal segments.", paramName);
        }
    }

    internal static byte[] CopyAndValidateHash(ImmutableArray<byte> boneLayoutHash, string paramName)
    {
        if (boneLayoutHash.IsDefault) throw new ArgumentException("BoneLayoutHash must be a 32-byte value.", paramName);
        var copy = boneLayoutHash.ToArray();
        if (copy.Length != 32) throw new ArgumentException("BoneLayoutHash must be exactly 32 bytes.", paramName);
        return copy;
    }

    internal static void WriteIdentityPayload(Stream stream, string skeletonPath, Guid? skeletonGuid, byte[] boneLayoutHash)
    {
        stream.WriteByte(SkeletonIdentityV1.Version);
        WriteFString(stream, skeletonPath, nameof(skeletonPath));
        WriteOptionalGuid(stream, skeletonGuid);
        stream.Write(boneLayoutHash, 0, boneLayoutHash.Length);
    }

    internal static void WriteFString(Stream stream, string value, string paramName)
    {
        var bytes = GetStrictUtf8Bytes(value + "\0", paramName);
        WriteInt32LittleEndian(stream, bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    internal static void WriteOptionalGuid(Stream stream, Guid? guid)
    {
        if (guid is null)
        {
            stream.WriteByte(0);
            return;
        }

        stream.WriteByte(1);
        var guidBytes = guid.Value.ToByteArray();
        stream.Write(guidBytes, 0, guidBytes.Length);
    }

    internal static void WriteInt32LittleEndian(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    internal static byte[] GetStrictUtf8Bytes(string value, string paramName)
    {
        try
        {
            return StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException($"{paramName} must be valid Unicode encodable as UTF-8.", paramName, exception);
        }
    }
}
