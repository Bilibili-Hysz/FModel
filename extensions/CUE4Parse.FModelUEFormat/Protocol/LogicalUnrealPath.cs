using System.Text.RegularExpressions;

namespace CUE4Parse.UeFormat.Protocol;

/// <summary>
/// Validates Unreal object paths emitted by the upstream UEFormat serializer.
/// Upstream builds may emit either rooted paths such as <c>/Game/...</c> or
/// project-relative paths such as <c>CodeWorld/Content/...</c>; both are logical
/// asset identities, not filesystem paths.
/// </summary>
public static class LogicalUnrealPath
{
    private static readonly Regex DriveAbsolutePathPattern = new("^[A-Za-z]:[/\\\\]", RegexOptions.CultureInvariant);

    public static void Validate(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Logical Unreal paths must be non-empty.", paramName);
        if (!string.Equals(path, path.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Logical Unreal paths must not include leading or trailing whitespace.", paramName);
        if (path.Contains('\0'))
            throw new ArgumentException("Logical Unreal paths must not contain NUL.", paramName);
        if (path.Contains('\\'))
            throw new ArgumentException("Logical Unreal paths must use forward slashes only.", paramName);
        if (DriveAbsolutePathPattern.IsMatch(path)
            || path.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.StartsWith("\\\\", StringComparison.Ordinal))
            throw new ArgumentException("Logical Unreal paths must not be absolute filesystem paths.", paramName);

        var segments = path.Split('/', StringSplitOptions.None);
        var rooted = path.StartsWith("/", StringComparison.Ordinal);
        var firstAssetSegment = rooted ? 1 : 0;
        if (segments.Length <= firstAssetSegment)
            throw new ArgumentException("Logical Unreal paths must contain at least one non-root segment.", paramName);
        if (!rooted
            && !path.StartsWith("Content/", StringComparison.Ordinal)
            && !path.Contains("/Content/", StringComparison.Ordinal))
            throw new ArgumentException("Unrooted Unreal paths must include a project Content segment.", paramName);

        for (var index = firstAssetSegment; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Length == 0 || segment is "." or "..")
                throw new ArgumentException("Logical Unreal paths must not contain empty or traversal segments.", paramName);
            if (segment.Any(char.IsControl))
                throw new ArgumentException("Logical Unreal paths must not contain control characters.", paramName);
        }
    }
}
