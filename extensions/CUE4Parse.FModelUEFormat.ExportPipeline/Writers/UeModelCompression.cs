using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using ZstdSharp;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.Writers;

/// <summary>
/// Reads and rebuilds the UEFormat file envelope used by UEMODEL files.
/// The upstream exporter compresses the complete chunk stream, so V3 extensions
/// must be inserted into the uncompressed payload and the envelope rebuilt.
/// </summary>
public static class UeModelCompression
{
    private static ReadOnlySpan<byte> Magic => "UEFORMAT"u8;
    private const int ZstdCompressionLevel = 6;

    /// <summary>
    /// Appends bytes to the logical UEMODEL chunk stream while preserving the
    /// original compression mode (uncompressed, GZIP, or ZSTD).
    /// </summary>
    public static byte[] AppendPayload(ReadOnlyMemory<byte> ueModelBytes, ReadOnlyMemory<byte> suffix)
    {
        var envelope = Read(ueModelBytes);
        var payload = new byte[envelope.Payload.Length + suffix.Length];
        envelope.Payload.AsSpan().CopyTo(payload);
        suffix.Span.CopyTo(payload.AsSpan(envelope.Payload.Length));
        return Build(envelope, payload);
    }

    /// <summary>
    /// Returns a validation view with the same logical chunk stream but without
    /// the compression envelope. The exported file itself remains compressed.
    /// </summary>
    public static byte[] ToUncompressed(ReadOnlyMemory<byte> ueModelBytes)
    {
        var envelope = Read(ueModelBytes);
        return envelope.IsCompressed
            ? Build(envelope with { CompressionFormat = null }, envelope.Payload)
            : ueModelBytes.ToArray();
    }
    internal static ParsedEnvelope Read(ReadOnlyMemory<byte> bytes)
    {
        var reader = new Reader(bytes.Span);
        if (!reader.ReadBytes(8, "UEFORMAT magic").SequenceEqual(Magic))
            throw new InvalidDataException("UEFORMAT magic is invalid.");

        var identifier = reader.ReadString("identifier");
        var version = reader.ReadByte("file version");
        var objectName = reader.ReadString("object name");
        var compressed = reader.ReadBoolean("compression flag");
        string? compressionFormat = null;
        var uncompressedSize = -1;
        var compressedSize = -1;
        if (compressed)
        {
            compressionFormat = reader.ReadString("compression format");
            uncompressedSize = reader.ReadInt32("uncompressed size");
            compressedSize = reader.ReadInt32("compressed size");
            if (uncompressedSize < 0 || compressedSize < 0)
                throw new InvalidDataException("UEFormat compression sizes must be non-negative.");
            if (compressedSize != reader.Remaining.Length)
                throw new InvalidDataException("UEFormat compressed size does not match the remaining file.");
        }

        var encodedPayload = reader.ReadBytes(reader.Remaining.Length, "UEFormat payload").ToArray();
        var payload = compressed
            ? Decompress(encodedPayload, compressionFormat!, uncompressedSize)
            : encodedPayload;

        return new ParsedEnvelope(identifier, version, objectName, compressionFormat, payload);
    }

    internal static byte[] Build(ParsedEnvelope envelope, ReadOnlySpan<byte> payload)
    {
        var compressed = envelope.CompressionFormat is not null;
        var encodedPayload = compressed
            ? Compress(payload, envelope.CompressionFormat!)
            : payload.ToArray();

        var result = new List<byte>(64 + encodedPayload.Length);
        result.AddRange(Magic.ToArray());
        WriteString(result, envelope.Identifier);
        result.Add(envelope.Version);
        WriteString(result, envelope.ObjectName);
        result.Add(compressed ? (byte)1 : (byte)0);
        if (compressed)
        {
            WriteString(result, envelope.CompressionFormat!);
            WriteInt32(result, payload.Length);
            WriteInt32(result, encodedPayload.Length);
        }
        result.AddRange(encodedPayload);
        return result.ToArray();
    }

    private static byte[] Decompress(byte[] encodedPayload, string compressionFormat, int expectedSize)
    {
        byte[] result;
        switch (compressionFormat.ToUpperInvariant())
        {
            case "ZSTD":
                using (var decompressor = new Decompressor())
                {
                    result = decompressor.Unwrap(encodedPayload).ToArray();
                }
                break;
            case "GZIP":
                using (var source = new MemoryStream(encodedPayload, writable: false))
                using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                using (var destination = new MemoryStream(expectedSize > 0 ? expectedSize : 0))
                {
                    gzip.CopyTo(destination);
                    result = destination.ToArray();
                }
                break;
            default:
                throw new InvalidDataException($"Unsupported UEFormat compression format: {compressionFormat}.");
        }

        if (result.Length != expectedSize)
            throw new InvalidDataException($"UEFormat decompressed size {result.Length} does not match header size {expectedSize}.");
        return result;
    }

    private static byte[] Compress(ReadOnlySpan<byte> payload, string compressionFormat)
    {
        switch (compressionFormat.ToUpperInvariant())
        {
            case "ZSTD":
                using (var compressor = new Compressor(ZstdCompressionLevel))
                {
                    return compressor.Wrap(payload.ToArray()).ToArray();
                }
            case "GZIP":
                using (var destination = new MemoryStream())
                {
                    using (var gzip = new GZipStream(destination, CompressionMode.Compress, leaveOpen: true))
                    {
                        gzip.Write(payload);
                    }
                    return destination.ToArray();
                }
            default:
                throw new InvalidDataException($"Unsupported UEFormat compression format: {compressionFormat}.");
        }
    }

    private static void WriteString(List<byte> destination, string value)
    {
        var encoded = new UTF8Encoding(false, true).GetBytes(value);
        WriteInt32(destination, encoded.Length);
        destination.AddRange(encoded);
    }

    private static void WriteInt32(List<byte> destination, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        destination.AddRange(buffer.ToArray());
    }

    internal sealed record ParsedEnvelope(
        string Identifier,
        byte Version,
        string ObjectName,
        string? CompressionFormat,
        byte[] Payload)
    {
        public bool IsCompressed => CompressionFormat is not null;
    }

    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> _remaining = bytes;

        public ReadOnlySpan<byte> Remaining => _remaining;

        public byte ReadByte(string description) => ReadBytes(1, description)[0];
        public bool ReadBoolean(string description) => ReadByte(description) switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidDataException($"Invalid {description}.")
        };
        public int ReadInt32(string description) => BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(4, description));

        public string ReadString(string description)
        {
            var length = ReadInt32($"{description} length");
            if (length == 0)
                return string.Empty;
            if (length > 0)
            {
                var bytes = ReadBytes(length, description);
                try
                {
                    return new UTF8Encoding(false, true).GetString(bytes);
                }
                catch (DecoderFallbackException exception)
                {
                    throw new InvalidDataException($"Invalid {description} UTF-8.", exception);
                }
            }

            var byteLength = checked(-length * sizeof(char));
            var bytes16 = ReadBytes(byteLength, description);
            try
            {
                return Encoding.Unicode.GetString(bytes16).TrimEnd('\0');
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException($"Invalid {description} UTF-16.", exception);
            }
        }

        public ReadOnlySpan<byte> ReadBytes(int length, string description)
        {
            if (length < 0 || length > _remaining.Length)
                throw new InvalidDataException($"{description} exceeds remaining input.");
            var result = _remaining[..length];
            _remaining = _remaining[length..];
            return result;
        }
    }
}
