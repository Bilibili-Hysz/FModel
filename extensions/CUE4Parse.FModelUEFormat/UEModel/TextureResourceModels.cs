using System.Collections.Immutable;
using System.Text;

namespace CUE4Parse.UeFormat.UEModel;

public enum PbrMapKind : byte
{
    BaseColor = 0,
    Normal = 1,
    Orm = 2
}

/// <summary>Filesystem resolution mode for an identity-only texture resource.</summary>
public enum FModelTextureResourceLocation : byte
{
    Relative = 1,
    Bundle = 2,
    Embedded = 3,
    ExportRoot = 4
}

public sealed class TextureResource : IEquatable<TextureResource>
{
    private readonly byte[] _sha256;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public TextureResource(string stableId, string logicalResourceUri, int byteLength, ImmutableArray<byte> sha256, bool isEmbedded)
        : this(stableId, logicalResourceUri, byteLength, sha256, isEmbedded,
            isEmbedded ? FModelTextureResourceLocation.Embedded : FModelTextureResourceLocation.ExportRoot)
    {
    }

    public TextureResource(
        string stableId,
        string logicalResourceUri,
        int byteLength,
        ImmutableArray<byte> sha256,
        bool isEmbedded,
        FModelTextureResourceLocation locationMode)
    {
        if (MaterialLinkSemanticValidation.IsBlankOrContainsNul(stableId))
            throw new ArgumentException("StableId must not be blank or contain NUL.", nameof(stableId));
        if (!Enum.IsDefined(locationMode))
            throw new ArgumentOutOfRangeException(nameof(locationMode));
        if (isEmbedded && locationMode != FModelTextureResourceLocation.Embedded)
            throw new ArgumentException("Embedded textures must use Embedded location mode.", nameof(locationMode));
        if (!isEmbedded && locationMode == FModelTextureResourceLocation.Embedded)
            throw new ArgumentException("Non-embedded textures cannot use Embedded location mode.", nameof(locationMode));
        if (!MaterialLinkSemanticValidation.IsLogicalUri(logicalResourceUri, locationMode == FModelTextureResourceLocation.Relative))
            throw new ArgumentException("LogicalResourceUri must be a safe logical URI.", nameof(logicalResourceUri));
        ValidateStrictUtf8(stableId, nameof(stableId), "StableId must be valid strict UTF-8 text.");
        ValidateStrictUtf8(logicalResourceUri, nameof(logicalResourceUri), "LogicalResourceUri must be valid strict UTF-8 text.");
        if (byteLength < 0)
            throw new ArgumentOutOfRangeException(nameof(byteLength), "ByteLength must be non-negative.");
        if (sha256.IsDefault)
            throw new ArgumentException("Sha256 must be exactly 32 bytes.", nameof(sha256));

        var copiedHash = sha256.ToArray();
        if (copiedHash.Length != 32)
            throw new ArgumentException("Sha256 must be exactly 32 bytes.", nameof(sha256));

        StableId = stableId;
        LogicalResourceUri = logicalResourceUri;
        ByteLength = byteLength;
        IsEmbedded = isEmbedded;
        LocationMode = locationMode;
        _sha256 = copiedHash;
    }

    public string StableId { get; }
    public string LogicalResourceUri { get; }
    public int ByteLength { get; }
    public bool IsEmbedded { get; }
    public FModelTextureResourceLocation LocationMode { get; }
    public ImmutableArray<byte> Sha256 => ImmutableArray.Create(_sha256);

    public bool Equals(TextureResource? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        return string.Equals(StableId, other.StableId, StringComparison.Ordinal)
            && string.Equals(LogicalResourceUri, other.LogicalResourceUri, StringComparison.Ordinal)
            && ByteLength == other.ByteLength
            && IsEmbedded == other.IsEmbedded
            && LocationMode == other.LocationMode
            && _sha256.AsSpan().SequenceEqual(other._sha256);
    }

    public override bool Equals(object? obj) => obj is TextureResource other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StableId, StringComparer.Ordinal);
        hash.Add(LogicalResourceUri, StringComparer.Ordinal);
        hash.Add(ByteLength);
        hash.Add(IsEmbedded);
        hash.Add(LocationMode);
        foreach (var value in _sha256) hash.Add(value);
        return hash.ToHashCode();
    }

    internal static void ValidateStrictUtf8(string value, string paramName, string message)
    {
        try
        {
            _ = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException ex)
        {
            throw new ArgumentException(message, paramName, ex);
        }
    }
}

public sealed class PbrTextureBinding : IEquatable<PbrTextureBinding>
{
    public PbrTextureBinding(string materialId, PbrMapKind mapKind, string textureResourceId, bool isSrgb)
    {
        if (MaterialLinkSemanticValidation.IsBlankOrContainsNul(materialId))
            throw new ArgumentException("MaterialId must not be blank or contain NUL.", nameof(materialId));
        if (!Enum.IsDefined(mapKind))
            throw new ArgumentOutOfRangeException(nameof(mapKind), "MapKind must be BaseColor, Normal, or Orm.");
        if (MaterialLinkSemanticValidation.IsBlankOrContainsNul(textureResourceId))
            throw new ArgumentException("TextureResourceId must not be blank or contain NUL.", nameof(textureResourceId));
        TextureResource.ValidateStrictUtf8(materialId, nameof(materialId), "MaterialId must be valid strict UTF-8 text.");
        TextureResource.ValidateStrictUtf8(textureResourceId, nameof(textureResourceId), "TextureResourceId must be valid strict UTF-8 text.");

        MaterialId = materialId;
        MapKind = mapKind;
        TextureResourceId = textureResourceId;
        IsSrgb = isSrgb;
    }

    public string MaterialId { get; }
    public PbrMapKind MapKind { get; }
    public string TextureResourceId { get; }
    public bool IsSrgb { get; }

    public bool Equals(PbrTextureBinding? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        return string.Equals(MaterialId, other.MaterialId, StringComparison.Ordinal)
            && MapKind == other.MapKind
            && string.Equals(TextureResourceId, other.TextureResourceId, StringComparison.Ordinal)
            && IsSrgb == other.IsSrgb;
    }

    public override bool Equals(object? obj) => obj is PbrTextureBinding other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.Ordinal.GetHashCode(MaterialId),
        MapKind,
        StringComparer.Ordinal.GetHashCode(TextureResourceId),
        IsSrgb);
}

public sealed record TextureResourceSet
{
    public TextureResourceSet(ImmutableArray<TextureResource> textures, ImmutableArray<PbrTextureBinding> bindings)
    {
        if (textures.IsDefault)
            throw new ArgumentException("Textures must not be a default ImmutableArray.", nameof(textures));
        if (bindings.IsDefault)
            throw new ArgumentException("Bindings must not be a default ImmutableArray.", nameof(bindings));
        foreach (var texture in textures)
        {
            if (texture is null)
                throw new ArgumentException("Textures must not contain null elements.", nameof(textures));
        }
        foreach (var binding in bindings)
        {
            if (binding is null)
                throw new ArgumentException("Bindings must not contain null elements.", nameof(bindings));
        }

        Textures = textures;
        Bindings = bindings;
    }

    public ImmutableArray<TextureResource> Textures { get; }
    public ImmutableArray<PbrTextureBinding> Bindings { get; }
}
