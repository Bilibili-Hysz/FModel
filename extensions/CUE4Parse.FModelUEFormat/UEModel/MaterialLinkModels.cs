using System.Collections.Immutable;

namespace CUE4Parse.UeFormat.UEModel;

public sealed record StaticMeshMaterialLink(
    int SlotIndex,
    int SourceMaterialIndex,
    string MaterialName,
    string SourceIdentity,
    string MaterialJsonUri);

public sealed record StaticMeshMaterialLinkSet(
    int SourceMaterialCount,
    ImmutableArray<StaticMeshMaterialLink> Links);
