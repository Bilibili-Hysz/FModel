using CUE4Parse.UeFormat.Protocol;

namespace CUE4Parse.UeFormat.UEModel;

internal static class MaterialLinkSemanticValidation
{
    internal static bool IsBlankOrContainsNul(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains('\0');

    internal static bool IsLogicalUri(string? value, bool allowLeadingParents = false) =>
        LogicalResourceUri.TryNormalize(value, out _, allowLeadingParents);
}
