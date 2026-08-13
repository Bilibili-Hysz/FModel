using System.Text.Json.Serialization;

namespace CUE4Parse.UeFormat.Protocol;

public sealed record SkeletonIdentityRecord
{
    public SkeletonIdentityRecord(string skeletonId, string sha256, IEnumerable<string> aliases)
    {
        SkeletonId = skeletonId; Sha256 = sha256;
        Aliases = Array.AsReadOnly((aliases ?? throw new ArgumentNullException(nameof(aliases))).ToArray());
    }
    [JsonPropertyOrder(1)] public string SkeletonId { get; }
    [JsonPropertyOrder(2)] public string Sha256 { get; }
    [JsonPropertyOrder(3)] public IReadOnlyList<string> Aliases { get; }
}
