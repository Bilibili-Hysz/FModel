namespace CUE4Parse.UeFormat.Protocol;

public sealed record UeFormatExtensionChunk
{
    public UeFormatExtensionChunk(string id, int count, ReadOnlyMemory<byte> payload)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An extension chunk identifier is required.", nameof(id));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Chunk counts must be non-negative.");

        Id = id;
        Count = count;
        _payload = payload.IsEmpty ? [] : payload.ToArray();
    }

    private readonly byte[] _payload;

    public string Id { get; }
    public int Count { get; }
    public ReadOnlyMemory<byte> Payload => _payload.ToArray();
}
