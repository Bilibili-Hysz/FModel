using System.Buffers.Binary;
using System.Text;

namespace CUE4Parse.UeFormat.UEModel;

public static class FModelMaterialLinkChunkWriter
{
    private const string ChunkName = "FMODEL_MATERIALS";
    private const byte Version = 2;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static byte[] WriteV2(StaticMeshMaterialLinkSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (set.SourceMaterialCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(set), "SourceMaterialCount must be positive.");
        if (set.Links.IsDefault)
            throw new ArgumentException("Links must not be a default ImmutableArray.", nameof(set));

        foreach (var link in set.Links)
        {
            if (link is null)
                throw new ArgumentException("Links must not contain null elements.", nameof(set));
        }

        var links = set.Links.OrderBy(link => link.SlotIndex).ToArray();
        var previousSlot = -1;
        foreach (var link in links)
        {
            if (link.SlotIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(set), "SlotIndex must be non-negative.");
            if (link.SlotIndex == previousSlot)
                throw new ArgumentException("SlotIndex values must be unique.", nameof(set));
            if (link.SourceMaterialIndex < 0 || link.SourceMaterialIndex >= set.SourceMaterialCount)
                throw new ArgumentOutOfRangeException(nameof(set), "SourceMaterialIndex is outside SourceMaterialCount.");
            if (MaterialLinkSemanticValidation.IsBlankOrContainsNul(link.MaterialName))
                throw new ArgumentException("MaterialName must not be blank.", nameof(set));
            if (MaterialLinkSemanticValidation.IsBlankOrContainsNul(link.SourceIdentity))
                throw new ArgumentException("SourceIdentity must not be blank.", nameof(set));
            if (!MaterialLinkSemanticValidation.IsLogicalUri(link.MaterialJsonUri, allowLeadingParents: true))
                throw new ArgumentException("MaterialJsonUri must be a non-rooted logical URI.", nameof(set));
            previousSlot = link.SlotIndex;
        }

        var payload = new List<byte> { Version };
        WriteInt32(payload, links.Length);
        foreach (var link in links)
        {
            WriteInt32(payload, link.SlotIndex);
            WriteInt32(payload, link.SourceMaterialIndex);
            WriteString(payload, link.MaterialName);
            WriteString(payload, link.SourceIdentity);
            WriteString(payload, link.MaterialJsonUri);
            WriteInt32(payload, 0);
        }

        var chunk = new List<byte>();
        WriteString(chunk, ChunkName);
        WriteInt32(chunk, links.Length);
        WriteInt32(chunk, payload.Count);
        chunk.AddRange(payload);
        return chunk.ToArray();
    }

    // .NET BinaryPrimitives makes the UEFormat v2 little-endian wire order explicit.
    private static void WriteInt32(List<byte> destination, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        destination.AddRange(buffer.ToArray());
    }

    private static void WriteString(List<byte> destination, string value)
    {
        var encoded = Utf8.GetBytes(value);
        WriteInt32(destination, encoded.Length);
        destination.AddRange(encoded);
    }
}
