using CUE4Parse_Conversion.Writers.UEFormat;

var customMountIdentity = FModelSkeletonIdentity.NormalizeLogicalUnrealPath(
    "CodeWorld/Content/Character/Animal/ArrowFish/Model/SKEL_EC_ArrowFish.SKEL_EC_ArrowFish",
    "customMountIdentity");
var rootedIdentity = FModelSkeletonIdentity.NormalizeLogicalUnrealPath(
    "/Game/Characters/Hero/SK_Hero.SK_Hero",
    "rootedIdentity");

var invalidPaths = new[]
{
    "",
    " /Game/Characters/Hero/SK_Hero.SK_Hero",
    "C:/Exports/SK_Hero.uasset",
    "file:///C:/Exports/SK_Hero.uasset",
    "//server/share/SK_Hero.uasset",
    "Game\\Characters\\Hero\\SK_Hero.SK_Hero",
    "/Game/Characters/../Hero/SK_Hero.SK_Hero",
    "/Game//Hero/SK_Hero.SK_Hero",
    "/Game/Characters/\tHero/SK_Hero.SK_Hero",
};
var rejectedPaths = invalidPaths.Count(path =>
{
    try
    {
        FModelSkeletonIdentity.NormalizeLogicalUnrealPath(path, "invalidIdentityPath");
        return false;
    }
    catch (ArgumentException)
    {
        return true;
    }
});

Console.WriteLine($"customMountIdentity={customMountIdentity}");
Console.WriteLine($"rootedIdentity={rootedIdentity}");
Console.WriteLine($"rejectedPaths={rejectedPaths}/{invalidPaths.Length}");

return customMountIdentity == "/CodeWorld/Content/Character/Animal/ArrowFish/Model/SKEL_EC_ArrowFish.SKEL_EC_ArrowFish"
    && rootedIdentity == "/Game/Characters/Hero/SK_Hero.SK_Hero"
    && rejectedPaths == invalidPaths.Length
    ? 0
    : 1;
