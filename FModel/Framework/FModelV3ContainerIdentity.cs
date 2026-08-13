#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FModel.Framework;

/// <summary>
/// New-pipeline-safe container identity and destination helpers.
/// The old FModel branch used the same concepts, but this version deliberately
/// keeps them independent from UserSettings, UI state, and legacy extraction APIs.
/// </summary>
internal static class FModelV3ContainerIdentity
{
    public static string NormalizeContainerPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Container path is required.", nameof(path));

        var candidate = path.Trim()
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(candidate);
        var root = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrEmpty(root) && string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            return root;

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static string GetContainerName(string containerPath)
        => Path.GetFileName(NormalizeContainerPath(containerPath));

    public static string GetContainerDirectoryName(string containerName)
    {
        if (string.IsNullOrWhiteSpace(containerName))
            throw new ArgumentException("Container name is required.", nameof(containerName));

        var name = Path.GetFileName(containerName.Trim());
        var extension = Path.GetExtension(name);
        return extension.Equals(".pak", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".utoc", StringComparison.OrdinalIgnoreCase)
            ? name[..^extension.Length]
            : name;
    }

    public static bool IsPackageExtension(string extension)
    {
        var normalized = extension.Trim().TrimStart('.');
        return normalized.Equals("uasset", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("umap", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetSourceDirectoryLabel(string containerPath, string gameDirectory)
    {
        var normalizedContainer = NormalizeContainerPath(containerPath);
        var normalizedGame = NormalizeContainerPath(gameDirectory);
        var parent = Directory.GetParent(normalizedContainer)?.FullName ?? normalizedContainer;

        if (parent.Equals(normalizedGame, StringComparison.OrdinalIgnoreCase))
            return parent;

        var prefix = normalizedGame.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedGame
            : normalizedGame + Path.DirectorySeparatorChar;
        return parent.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? parent[prefix.Length..]
            : parent;
    }

    public static HashSet<string> CreateContainerPathSet(IEnumerable<string> containerPaths)
    {
        ArgumentNullException.ThrowIfNull(containerPaths);
        return new(
            containerPaths
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeContainerPath),
            StringComparer.OrdinalIgnoreCase);
    }

    public static bool HasDuplicateBasenames(IEnumerable<string> containerPaths)
    {
        ArgumentNullException.ThrowIfNull(containerPaths);
        return containerPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(GetContainerName)
            .GroupBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Any(static group => group.Count() > 1);
    }

    public static string CombineContainedDirectory(string root, string directoryName)
    {
        var normalizedRoot = NormalizeContainerPath(root);
        if (string.IsNullOrWhiteSpace(directoryName) ||
            directoryName is "." or ".." ||
            directoryName.Contains(Path.DirectorySeparatorChar) ||
            directoryName.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("Directory name must be a single safe path segment.", nameof(directoryName));

        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, directoryName));
        var prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Destination escapes the selected root.", nameof(directoryName));

        return candidate;
    }
}
