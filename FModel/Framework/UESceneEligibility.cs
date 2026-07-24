using System;
using System.Collections.Generic;
using System.Linq;
using CUE4Parse.FileProvider.Objects;
using FModel.ViewModels;

namespace FModel.Framework;

public static class UESceneEligibility
{
    public static bool IsEligible(GameFile entry)
    {
        return entry?.IsUePackage == true && entry.Extension.Equals("umap", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsEligible(GameFileViewModel entry)
    {
        return entry?.AssetCategory == EAssetCategory.World || IsEligible(entry?.Asset);
    }

    public static GameFile[] FilterEligible(IEnumerable<object> entries)
    {
        return entries.Select(static entry => entry switch
            {
                GameFileViewModel viewModel when IsEligible(viewModel) => viewModel.Asset,
                GameFile gameFile when IsEligible(gameFile) => gameFile,
                _ => null
            })
            .Where(static entry => entry is not null)
            .Distinct()
            .ToArray();
    }
}
