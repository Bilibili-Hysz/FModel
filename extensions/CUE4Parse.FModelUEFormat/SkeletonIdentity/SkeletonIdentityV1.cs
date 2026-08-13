using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CUE4Parse.UeFormat.Protocol;

public sealed record SkeletonBone(string Name, int ParentIndex);

public sealed class SkeletonIdentityV1 : IEquatable<SkeletonIdentityV1>
{
    public const byte Version = 1;
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly byte[] LayoutHashPrefix = StrictUtf8.GetBytes("fmodel-skeleton-layout-v1\0");
    private readonly byte[] _boneLayoutHash;

    public SkeletonIdentityV1(string skeletonPath, Guid? skeletonGuid, ImmutableArray<byte> boneLayoutHash)
    {
        SkeletonPath = skeletonPath ?? throw new ArgumentNullException(nameof(skeletonPath));
        SkeletonGuid = skeletonGuid;
        if (boneLayoutHash.IsDefault) throw new ArgumentException("BoneLayoutHash must be a 32-byte value.", nameof(boneLayoutHash));
        var copiedHash = boneLayoutHash.ToArray();
        if (copiedHash.Length != 32) throw new ArgumentException("BoneLayoutHash must be exactly 32 bytes.", nameof(boneLayoutHash));
        _boneLayoutHash = copiedHash;
    }

    public string SkeletonPath { get; }
    public Guid? SkeletonGuid { get; }
    public ImmutableArray<byte> BoneLayoutHash => ImmutableArray.Create(_boneLayoutHash);

    public bool Equals(SkeletonIdentityV1? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        return string.Equals(SkeletonPath, other.SkeletonPath, StringComparison.Ordinal)
            && Nullable.Equals(SkeletonGuid, other.SkeletonGuid)
            && _boneLayoutHash.AsSpan().SequenceEqual(other._boneLayoutHash);
    }

    public override bool Equals(object? obj) => obj is SkeletonIdentityV1 other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SkeletonPath, StringComparer.Ordinal);
        hash.Add(SkeletonGuid);
        for (var index = 0; index < _boneLayoutHash.Length; index++) hash.Add(_boneLayoutHash[index]);
        return hash.ToHashCode();
    }

    public static ImmutableArray<byte> ComputeLayoutHash(IEnumerable<SkeletonBone> bones)
    {
        ArgumentNullException.ThrowIfNull(bones);
        var materialized = bones.ToArray();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        using var buffer = new MemoryStream();
        buffer.Write(LayoutHashPrefix, 0, LayoutHashPrefix.Length);
        WriteInt32LittleEndian(buffer, materialized.Length);

        for (var index = 0; index < materialized.Length; index++)
        {
            var bone = materialized[index] ?? throw new ArgumentException("Bones cannot contain null entries.", nameof(bones));
            if (string.IsNullOrWhiteSpace(bone.Name)) throw new ArgumentException("Bone names must be non-empty and non-whitespace.", nameof(bones));
            if (bone.Name.Contains('\0')) throw new ArgumentException("Bone names must not contain NUL.", nameof(bones));
            if (!seenNames.Add(bone.Name)) throw new ArgumentException($"Duplicate bone name is not allowed: '{bone.Name}'.", nameof(bones));
            if (bone.ParentIndex < -1 || bone.ParentIndex >= index)
                throw new ArgumentOutOfRangeException(nameof(bones), $"ParentIndex must be -1 or reference a prior bone. Index={index}, ParentIndex={bone.ParentIndex}.");

            var nameBytes = GetStrictUtf8Bytes(bone.Name, nameof(bones));
            WriteInt32LittleEndian(buffer, nameBytes.Length);
            buffer.Write(nameBytes, 0, nameBytes.Length);
            WriteInt32LittleEndian(buffer, bone.ParentIndex);
        }

        return ImmutableArray.CreateRange(SHA256.HashData(buffer.ToArray()));
    }

    private static void WriteInt32LittleEndian(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static byte[] GetStrictUtf8Bytes(string value, string paramName)
    {
        try
        {
            return StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException($"{paramName} must contain only valid Unicode encodable as UTF-8.", paramName, exception);
        }
    }
}
