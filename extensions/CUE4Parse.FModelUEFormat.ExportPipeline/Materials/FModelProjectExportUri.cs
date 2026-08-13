namespace CUE4Parse.FModelUEFormat.ExportPipeline.Materials;

/// <summary>
/// Projects provider-fixed project paths into the stable Content-relative
/// namespace used by compatibility policies. The active UEMODEL profile is
/// owner-relative; this helper is retained without depending on UESCENE.
/// </summary>
public static class FModelProjectExportUri
{
    public static string FromFixedExportPath(string fixedExportPath)
    {
        var normalized = NormalizePath(fixedExportPath, nameof(fixedExportPath));
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Split('/', StringSplitOptions.None).Any(segment => segment.Length == 0))
        {
            throw new ArgumentException(
                "Fixed export path must be a relative slash-separated path.",
                nameof(fixedExportPath));
        }

        var segments = normalized.Split('/', StringSplitOptions.None);
        if (segments.Any(IsUnsafeSegment))
            throw new ArgumentException("Path contains an unsafe or empty segment.", nameof(fixedExportPath));

        string[] contentRelative;
        if (string.Equals(segments[0], "Content", StringComparison.Ordinal))
        {
            contentRelative = segments;
        }
        else if (segments.Length >= 2 &&
                 !IsReservedMount(segments[0]) &&
                 string.Equals(segments[1], "Content", StringComparison.Ordinal))
        {
            contentRelative = segments[1..];
        }
        else
        {
            throw new ArgumentException(
                "Path must be rooted at a project Content directory.",
                nameof(fixedExportPath));
        }

        if (contentRelative.Length < 2)
            throw new ArgumentException(
                "Fixed export path must identify a resource below Content/.",
                nameof(fixedExportPath));
        return string.Join('/', contentRelative);
    }

    public static bool IsContentRootedUnrealPath(string unrealPath)
    {
        var normalized = NormalizePath(unrealPath, nameof(unrealPath));
        if (!normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Unreal path must be an absolute single-rooted path.",
                nameof(unrealPath));
        }

        var segments = normalized.Split('/', StringSplitOptions.None);
        if (segments.Length < 3 || segments[0].Length != 0 || segments[1..].Any(IsUnsafeSegment))
            throw new ArgumentException(
                "Unreal path contains an unsafe or empty segment.",
                nameof(unrealPath));

        return normalized.StartsWith("/Game/", StringComparison.Ordinal) ||
               (segments.Length >= 3 &&
                !IsReservedMount(segments[1]) &&
                string.Equals(segments[2], "Content", StringComparison.Ordinal));
    }

    private static string NormalizePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0'))
            throw new ArgumentException("Path is required and cannot contain NUL.", parameterName);
        if (path.Contains('\\'))
            throw new ArgumentException("Path must use POSIX separators.", parameterName);
        if (path.Contains(':'))
            throw new ArgumentException("Path must not contain a URI scheme or drive prefix.", parameterName);
        if (path.Any(char.IsControl))
            throw new ArgumentException("Path must not contain control characters.", parameterName);
        return path;
    }

    private static bool IsUnsafeSegment(string segment) => segment is "" or "." or "..";

    private static bool IsReservedMount(string segment) =>
        segment.Equals("Engine", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Plugin", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Plugins", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Script", StringComparison.OrdinalIgnoreCase);
}
