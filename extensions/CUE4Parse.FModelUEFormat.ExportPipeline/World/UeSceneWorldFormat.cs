using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Formats.World;
using CUE4Parse_Conversion.UEScene.World;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.World;

/// <summary>
/// P2 world backend. The provider is intentionally injectable so P3 can bind
/// actual V3 UEMODEL/material/texture producer snapshots without changing the
/// WorldDto traversal or UEScene wire builder.
/// </summary>
public sealed class UeSceneWorldFormat : IWorldExportFormatWithPathResolver
{
    private readonly UeSceneWorldPlanner planner;
    private readonly UeSceneWorldDocumentBuilder documentBuilder = new();
    private readonly Action<UeSceneWorldPlan>? onPlanBuilt;

    public UeSceneWorldFormat(IUeSceneWorldAssetProvider assetProvider, Action<UeSceneWorldPlan>? onPlanBuilt = null)
    {
        planner = new UeSceneWorldPlanner(assetProvider);
        this.onPlanBuilt = onPlanBuilt;
    }

    public string DisplayName => "UEFormat World (.uescene)";
    public IWorldExportPathResolver PathResolver { get; } = UeSceneWorldPathResolver.Instance;

    public ExportFile Build(WorldDto dto, WorldAssetPaths paths)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(paths);
        var plan = planner.Plan(dto, paths);
        if (plan.Actors.Count == 0)
            throw new InvalidOperationException("UEScene World planner produced no actors.");

        // Build the binary before publishing the plan to the host. A failed wire
        // build must not leave a manifest candidate for a scene that never wrote.
        var bytes = documentBuilder.Write(plan);
        onPlanBuilt?.Invoke(plan);

        // The .umap suffix is retained for compatibility with the Game export
        // tree naming contract: <Map>.umap.uescene.
        return new ExportFile("uescene", bytes, ".umap");
    }
}
