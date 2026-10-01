using System.Security.Cryptography;
using CUE4Parse.FModelUEFormat.ExportPipeline.Materials;
using CUE4Parse.UeFormat.UEModel;
using CUE4Parse_Conversion;

namespace CUE4Parse.FModelUEFormat.ExportPipeline.Delivery;

/// <summary>Only immutable strings/lengths/32-byte digests, never UObjects or encoded buffers.</summary>
public sealed record FModelUeFormatDeliveryDeclaration(
    string RootObjectPath, string OwnerModelUri,
    IReadOnlyList<FModelTextureResourceProjection> Resources);

public sealed record FModelUeFormatDeliveryValidation(
    IReadOnlyList<ExportResult> Results,
    IReadOnlySet<string> DeclaredRoots,
    IReadOnlyDictionary<string, string> FailedRoots,
    int VerifiedTextureFileCount);

/// <summary>
/// Verify only resources actually declared by successfully serialized FMODEL_TEXTURES.
/// A current successful write result is mandatory; an old file cannot mask a failed
/// dependency. No official task, USD format or wire payload is changed here.
/// </summary>
public static class FModelUeFormatDeliveryVerifier
{
    public static FModelUeFormatDeliveryValidation Validate(
        IReadOnlyList<ExportResult> results, string exportDirectory,
        IReadOnlyList<FModelUeFormatDeliveryDeclaration> declarations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(declarations);
        var declaredRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (declarations.Count == 0)
            return new(results, declaredRoots, failures, 0);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(exportDirectory));
        var owners = new Dictionary<string, ExportResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in declarations) declaredRoots.Add(declaration.RootObjectPath);
        foreach (var result in results)
            if (declaredRoots.Contains(result.ObjectPath)) owners[result.ObjectPath] = result;

        var required = new Dictionary<string, RequiredFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Already-failed roots retain their original diagnostic; canceled or
            // unstarted work must never become success through this verifier.
            if (!owners.TryGetValue(declaration.RootObjectPath, out var owner) || !owner.Success)
                continue;
            try
            {
                var ownerPath = ResolveContained(root, root, declaration.OwnerModelUri, allowParents: false);
                if (!(owner.DiskFilePaths?.Any(path => SamePath(path, ownerPath)) ?? false) || !File.Exists(ownerPath))
                    throw new IOException("The declared owner model was not delivered by this operation.");
                RejectReparseDescendants(root, ownerPath);
                foreach (var resource in declaration.Resources)
                {
                    if (resource.IsEmbedded || resource.ByteLength < 0 || resource.Sha256.Count != 32)
                        throw new InvalidDataException("Invalid required texture delivery metadata.");
                    var baseDirectory = resource.LocationMode switch
                    {
                        FModelTextureResourceLocation.Relative => Path.GetDirectoryName(ownerPath)!,
                        FModelTextureResourceLocation.ExportRoot => root,
                        _ => throw new InvalidDataException("Unsupported required texture location mode.")
                    };
                    var path = ResolveContained(root, baseDirectory, resource.LogicalResourceUri,
                        resource.LocationMode == FModelTextureResourceLocation.Relative);
                    if (!required.TryGetValue(path, out var file))
                        required[path] = file = new RequiredFile(resource.ByteLength, resource.Sha256.ToArray());
                    if (file.Length != resource.ByteLength || !file.Hash.SequenceEqual(resource.Sha256))
                        file.ConflictingMetadata = true;
                    file.Owners.Add(declaration.RootObjectPath);
                }
            }
            catch (Exception error) when (IsResourceFailure(error))
            {
                failures.TryAdd(declaration.RootObjectPath, "UEFormat required-resource delivery failed: " + error.Message);
            }
        }

        // Index only requested paths, not a second copy of every session result.
        foreach (var result in results)
        {
            if (!result.Success) continue;
            foreach (var path in result.DiskFilePaths ?? Array.Empty<string>())
            {
                try
                {
                    if (required.TryGetValue(Path.GetFullPath(path), out var file)) file.CurrentWriteSucceeded = true;
                }
                catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    // Unrelated official results do not affect this scoped gate.
                }
            }
        }
        var verified = 0;
        var buffer = required.Count == 0 ? Array.Empty<byte>() : new byte[64 * 1024];
        foreach (var pair in required)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (pair.Value.ConflictingMetadata)
                    throw new InvalidDataException("Models declare conflicting metadata for a shared texture.");
                if (!pair.Value.CurrentWriteSucceeded)
                    throw new IOException("A required texture has no successful write result in this operation.");
                RejectReparseDescendants(root, pair.Key);
                using var stream = new FileStream(pair.Key, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length != pair.Value.Length)
                    throw new InvalidDataException("A required texture byte length does not match its model declaration.");
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hash.AppendData(buffer, 0, read);
                }
                if (!hash.GetHashAndReset().SequenceEqual(pair.Value.Hash))
                    throw new InvalidDataException("A required texture SHA-256 does not match its model declaration.");
                verified++;
            }
            catch (Exception error) when (IsResourceFailure(error))
            {
                foreach (var owner in pair.Value.Owners)
                    failures.TryAdd(owner, $"UEFormat required-resource delivery failed ({Path.GetFileName(pair.Key)}): " + error.Message);
            }
        }
        if (failures.Count == 0) return new(results, declaredRoots, failures, verified);
        // Success takes the zero-copy path. Allocate only shallow result references
        // on failure; keep written paths so the user can inspect partial outputs.
        var effectiveResults = new ExportResult[results.Count];
        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            effectiveResults[index] = result.Success && failures.TryGetValue(result.ObjectPath, out var error)
                ? result with { Success = false, Error = new IOException(error) }
                : result;
        }
        return new(effectiveResults, declaredRoots, failures, verified);
    }

    private static bool IsResourceFailure(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static bool SamePath(string value, string expected) =>
        string.Equals(Path.GetFullPath(value), expected, StringComparison.OrdinalIgnoreCase);

    private static string ResolveContained(string root, string baseDirectory, string uri, bool allowParents)
    {
        if (string.IsNullOrWhiteSpace(uri) || uri.Contains('\\') || uri.Contains(':') || uri.StartsWith('/') ||
            uri.Any(character => char.IsControl(character)))
            throw new InvalidDataException("Invalid required-resource URI.");
        var parts = uri.Split('/');
        var index = 0;
        if (allowParents) while (index < parts.Length && parts[index] == "..") index++;
        if (index == parts.Length || parts.Skip(index).Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Invalid required-resource URI components.");
        var path = Path.GetFullPath(Path.Combine(baseDirectory, uri.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A required-resource URI escapes the export root.");
        return path;
    }

    private static void RejectReparseDescendants(string root, string path)
    {
        // An explicitly selected root is accepted; its descendants cannot redirect
        // a declared owner/texture outside that root through symlinks or junctions.
        for (string? item = path; item is not null && !SamePath(item, root); item = Path.GetDirectoryName(item))
            if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("A required-resource path crosses a reparse point.");
    }

    private sealed class RequiredFile(long length, byte[] hash)
    {
        public long Length { get; } = length;
        public byte[] Hash { get; } = hash;
        public HashSet<string> Owners { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool CurrentWriteSucceeded, ConflictingMetadata;
    }
}
