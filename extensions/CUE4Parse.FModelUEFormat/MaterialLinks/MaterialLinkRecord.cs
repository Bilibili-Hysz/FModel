using System.Text.Json.Serialization;

namespace CUE4Parse.UeFormat.Protocol;

public sealed record MaterialLinkRecord(
    [property: JsonPropertyOrder(1)] string MaterialId,
    [property: JsonPropertyOrder(2)] string MaterialUri,
    [property: JsonPropertyOrder(3)] string RelativeTexturePath);
