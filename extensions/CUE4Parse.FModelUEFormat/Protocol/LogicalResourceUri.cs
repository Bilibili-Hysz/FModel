namespace CUE4Parse.UeFormat.Protocol;

/// <summary>
/// Canonical validation and resolution rules for logical export-resource URIs.
///
/// A logical URI is not a filesystem path and never carries a scheme, drive, or
/// rooted prefix. Owner-relative references may contain leading <c>..</c> segments,
/// but traversal after the first resource segment is never allowed.
/// </summary>
public static class LogicalResourceUri
{
    public static string Normalize(string value, bool allowLeadingParents = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Contains('\0'))
            throw new ArgumentException("Logical resource URIs must not contain NUL.", nameof(value));

        if (value.Contains('\\'))
            throw new ArgumentException("Logical resource URIs must use forward slashes only.", nameof(value));

        var normalized = value;
        if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains(':'))
            throw new ArgumentException("Logical resource URIs must be relative and scheme-free.", nameof(value));

        foreach (var character in normalized)
        {
            if (char.IsControl(character))
                throw new ArgumentException("Logical resource URIs must not contain control characters.", nameof(value));
        }

        var segments = normalized.Split('/', StringSplitOptions.None);
        var seenResourceSegment = false;
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment == ".")
                throw new ArgumentException("Logical resource URIs must not contain empty or current-directory segments.", nameof(value));
            if (segment == "..")
            {
                if (!allowLeadingParents || seenResourceSegment)
                    throw new ArgumentException("Logical resource URI traversal is only allowed in leading owner-relative segments.", nameof(value));
                continue;
            }

            seenResourceSegment = true;
        }

        if (!seenResourceSegment)
            throw new ArgumentException("Logical resource URIs must identify a resource after any leading parent segments.", nameof(value));

        return string.Join('/', segments);
    }

    public static bool TryNormalize(string? value, out string? normalized, bool allowLeadingParents = false)
    {
        try
        {
            normalized = value is null ? null : Normalize(value, allowLeadingParents);
            return normalized is not null;
        }
        catch (ArgumentException)
        {
            normalized = null;
            return false;
        }
    }

    /// <summary>Resolves a target logical URI relative to an owner file URI.</summary>
    public static string ResolveRelative(string ownerFileUri, string targetFileUri)
    {
        var owner = Normalize(ownerFileUri);
        var target = Normalize(targetFileUri);
        var ownerSegments = owner.Split('/');
        var targetSegments = target.Split('/');
        var ownerDirectoryLength = ownerSegments.Length - 1;

        var commonLength = 0;
        while (commonLength < ownerDirectoryLength
            && commonLength < targetSegments.Length
            && string.Equals(ownerSegments[commonLength], targetSegments[commonLength], StringComparison.Ordinal))
        {
            commonLength++;
        }

        var relativeSegments = new List<string>();
        for (var index = commonLength; index < ownerDirectoryLength; index++)
            relativeSegments.Add("..");
        for (var index = commonLength; index < targetSegments.Length; index++)
            relativeSegments.Add(targetSegments[index]);

        if (relativeSegments.Count == 0)
            relativeSegments.Add(targetSegments[^1]);

        return Normalize(string.Join('/', relativeSegments), allowLeadingParents: true);
    }
}
