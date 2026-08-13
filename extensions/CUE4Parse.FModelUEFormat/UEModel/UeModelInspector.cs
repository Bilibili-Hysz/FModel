using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CUE4Parse.UeFormat.Protocol;

namespace CUE4Parse.UeFormat.UEModel;

public static class UeModelInspector
{
    // V3 has no referenced upstream UEMODEL parser API; this reader validates standard UEFORMAT wire data directly.
    private static ReadOnlySpan<byte> Magic => "UEFORMAT"u8;
    private const byte CurrentSupportedFileVersion = 9;

    public static UeModelInspection Inspect(ReadOnlyMemory<byte> bytes)
    {
        var reader = new Reader(bytes.Span);
        if (!reader.ReadBytes(8).SequenceEqual(Magic)) throw Invalid("UEFORMAT magic is invalid.");
        if (reader.ReadString("identifier") != "UEMODEL") throw Invalid("UEFORMAT identifier is not UEMODEL.");
        var version = reader.ReadByte("file version");
        if (version != CurrentSupportedFileVersion) throw Invalid($"Unsupported UEFormat file version: {version}.");
        var objectName = reader.ReadString("object name");
        if (reader.ReadBoolean("compression flag")) throw Invalid("Compressed UEFormat is unsupported.");
        var lodCount = 0;
        var nonzeroGeometry = false;
        var seenLods = false;
        int? materialVersion = null;
        int? textureVersion = null;
        var materialLinks = ImmutableArray<InspectedMaterialLink>.Empty;
        var textureResources = ImmutableArray<InspectedTextureResource>.Empty;
        var pbrTextureBindings = ImmutableArray<InspectedPbrTextureBinding>.Empty;
        var materialSlots = ImmutableHashSet<int>.Empty;
        var standardMaterials = ImmutableArray<InspectedStandardMaterial>.Empty;
        var lodTopologies = ImmutableArray<InspectedLodTopology>.Empty;
        InspectedSkeletonTopology? skeletonTopology = null;
        SkeletonIdentityV1? skeletonIdentity = null;
        while (!reader.End)
        {
            var header = reader.ReadString("chunk header");
            var count = reader.ReadCount("chunk count");
            var payload = reader.ReadSlice(reader.ReadLength("chunk length"), "chunk payload");
            if (header == "LODS")
            {
                if (seenLods) throw Invalid("Duplicate LODS chunk.");
                seenLods = true;
                materialSlots = InspectLods(payload, count, out lodCount, out var hasNonzeroGeometry, out standardMaterials, out lodTopologies);
                nonzeroGeometry = hasNonzeroGeometry;
            }
            else if (header == "SKELETON")
            {
                if (skeletonTopology is not null) throw Invalid("Duplicate SKELETON chunk.");
                skeletonTopology = InspectSkeleton(payload, count);
            }
            else if (header == "FMODEL_MATERIALS")
            {
                if (materialVersion is not null) throw Invalid("Duplicate FMODEL_MATERIALS chunk.");
                var inspected = InspectMaterialChunkCore(payload, out var extensionVersion);
                if (count != inspected.Length) throw Invalid("FMODEL_MATERIALS envelope count does not match payload.");
                materialVersion = extensionVersion;
                materialLinks = inspected;
            }
            else if (header == "FMODEL_TEXTURES")
            {
                if (textureVersion is not null) throw Invalid("Duplicate FMODEL_TEXTURES chunk.");
                var inspected = FModelTextureChunkInspector.Inspect(payload);
                if (count != 1) throw Invalid("FMODEL_TEXTURES envelope count must be exactly 1.");
                textureVersion = inspected.Version;
                textureResources = inspected.Textures;
                pbrTextureBindings = inspected.Bindings;
            }
            else if (header == "FMODEL_SKELETON_IDENTITY")
            {
                if (skeletonIdentity is not null) throw Invalid("Duplicate FMODEL_SKELETON_IDENTITY chunk.");
                try
                {
                    skeletonIdentity = SkeletonIdentityInspector.ReadModelIdentity(new UeFormatExtensionChunk(header, count, payload.ToArray()));
                }
                catch (ArgumentException exception)
                {
                    throw Invalid($"Invalid FMODEL_SKELETON_IDENTITY chunk: {exception.Message}");
                }
            }
            else if (header.StartsWith("FMODEL_", StringComparison.Ordinal))
            {
                throw Invalid($"Unsupported material extension chunk: {header}.");
            }
        }

        if (!seenLods) throw Invalid("UEMODEL is missing its LODS chunk.");
        if (lodCount == 0) throw Invalid("UEMODEL must contain at least one LOD.");
        if (!nonzeroGeometry) throw Invalid("UEMODEL must contain at least one LOD with nonzero geometry.");
        if (materialVersion is null) throw Invalid("UEMODEL is missing required FMODEL_MATERIALS chunk.");
        var standardMaterialPaths = standardMaterials.ToDictionary(material => material.SlotIndex, material => material.UnrealMaterialPath);
        var materialLinkPaths = materialLinks.ToDictionary(link => link.SlotIndex, link => link.UnrealMaterialPath);
        if (!materialSlots.SetEquals(materialLinkPaths.Keys))
            throw Invalid("FMODEL_MATERIALS slots must exactly match standard LOD MATERIALS.");
        if (materialLinks.Any(link => !standardMaterialPaths.TryGetValue(link.SlotIndex, out var materialPath) || materialPath != link.UnrealMaterialPath))
            throw Invalid("FMODEL_MATERIALS path does not match standard LOD MATERIALS.");
        if (pbrTextureBindings.Any(binding => !materialLinks.Any(link => link.MaterialName == binding.MaterialId)))
            throw Invalid("FMODEL_TEXTURES binding material does not exist in FMODEL_MATERIALS.");
        if (skeletonIdentity is not null && skeletonTopology is null)
            throw Invalid("FMODEL_SKELETON_IDENTITY requires a standard SKELETON chunk.");
        if (skeletonTopology is not null)
        {
            if (skeletonIdentity is not null && skeletonIdentity.SkeletonPath != skeletonTopology.Identity!.SkeletonPath)
                throw Invalid("FMODEL_SKELETON_IDENTITY path does not match standard SKELETON metadata.");
            skeletonTopology = new InspectedSkeletonTopology(skeletonTopology.Bones, skeletonTopology.BoneCount, skeletonTopology.BoneLayoutHash, skeletonIdentity ?? skeletonTopology.Identity);
        }

        var validated = UeModelTopologyInspection.Validate(new(objectName, version, lodCount, nonzeroGeometry, materialSlots.Order().ToImmutableArray(), standardMaterials,
            materialVersion, materialLinks, textureVersion, textureResources, pbrTextureBindings, lodTopologies, skeletonTopology, Convert.ToHexString(SHA256.HashData(bytes.Span))));

        return validated;
    }

    internal static ImmutableArray<InspectedMaterialLink> InspectMaterialChunk(ReadOnlyMemory<byte> chunk)
    {
        var reader = new Reader(chunk.Span);
        if (reader.ReadString("FMODEL_MATERIALS chunk header") != "FMODEL_MATERIALS")
            throw Invalid("Expected exactly one FMODEL_MATERIALS chunk.");
        var count = reader.ReadCount("FMODEL_MATERIALS envelope count");
        var payload = reader.ReadSlice(reader.ReadLength("FMODEL_MATERIALS envelope length"), "FMODEL_MATERIALS envelope payload");
        if (!reader.End) throw Invalid("Unread trailing bytes after FMODEL_MATERIALS chunk.");
        var links = InspectMaterialChunkCore(payload, out _);
        if (count != links.Length) throw Invalid("FMODEL_MATERIALS envelope count does not match payload.");
        return links;
    }

    private static ImmutableHashSet<int> InspectLods(ReadOnlySpan<byte> payload, int count, out int lodCount, out bool hasNonzeroGeometry, out ImmutableArray<InspectedStandardMaterial> standardMaterials, out ImmutableArray<InspectedLodTopology> lodTopologies)
    {
        var reader = new Reader(payload);
        var topologies = ImmutableArray.CreateBuilder<InspectedLodTopology>(count);
        hasNonzeroGeometry = false;
        for (var index = 0; index < count; index++)
        {
            var name = reader.ReadString("LOD header");
            if (!IsCanonicalLodName(name))
                throw Invalid("Invalid LOD chunk name.");
            var lodPayload = reader.ReadSlice(reader.ReadLength("LOD length"), "LOD payload");
            var topology = InspectLod(lodPayload, ParseLodIndex(name), out var hasGeometry);
            topologies.Add(topology);
            hasNonzeroGeometry |= hasGeometry;
        }
        if (!reader.End) throw Invalid("Unread trailing bytes in LODS chunk.");
        lodCount = count;
        if (topologies.Count == 0)
        {
            standardMaterials = ImmutableArray<InspectedStandardMaterial>.Empty;
            lodTopologies = ImmutableArray<InspectedLodTopology>.Empty;
            return ImmutableHashSet<int>.Empty;
        }
        var referenceTopology = topologies[0];
        var referenceMaterials = referenceTopology.MaterialSlots.ToDictionary(slot => slot.SlotIndex, slot => slot.UnrealMaterialPath);
        if (topologies.Any(topology => !referenceMaterials.OrderBy(pair => pair.Key).SequenceEqual(
                topology.MaterialSlots.OrderBy(slot => slot.SlotIndex).Select(slot => new KeyValuePair<int, string>(slot.SlotIndex, slot.UnrealMaterialPath)))))
            throw Invalid("Standard LOD MATERIALS slot topology must match across all LODs.");
        standardMaterials = referenceMaterials
            .OrderBy(pair => pair.Key)
            .Select(pair => new InspectedStandardMaterial(pair.Key, pair.Value))
            .ToImmutableArray();
        lodTopologies = topologies.ToImmutable();
        return referenceMaterials.Keys.ToImmutableHashSet();
    }

    private static bool IsCanonicalLodName(string name)
    {
        if (!name.StartsWith("LOD", StringComparison.Ordinal) || name.Length == 3) return false;
        foreach (var character in name.AsSpan(3))
        {
            if (character is < '0' or > '9') return false;
        }
        return int.TryParse(name.AsSpan(3), out var lodIndex) && lodIndex >= 0;
    }

    private static int ParseLodIndex(string name) => int.Parse(name.AsSpan(3), System.Globalization.CultureInfo.InvariantCulture);

    private static InspectedLodTopology InspectLod(ReadOnlySpan<byte> payload, int lodIndex, out bool hasGeometry)
    {
        var reader = new Reader(payload);
        var vertexCount = 0;
        var indices = Array.Empty<int>();
        ReadOnlySpan<byte> materialsPayload = default;
        var materialCount = 0;
        int? normalCount = null;
        int? tangentCount = null;
        int[]? texCoordVertexCounts = null;
        int[]? vertexColorCounts = null;
        int[]? weightVertexIndices = null;
        int[]? morphVertexIndices = null;
        var seenVertices = false;
        var seenNormals = false;
        var seenTangents = false;
        var seenTexCoords = false;
        var seenIndices = false;
        var seenMaterials = false;
        var seenHeaders = new HashSet<string>(StringComparer.Ordinal);
        while (!reader.End)
        {
            var header = reader.ReadString("LOD child header");
            if (!seenHeaders.Add(header)) throw Invalid("Duplicate LOD child chunk.");
            var count = reader.ReadCount("LOD child count");
            var child = reader.ReadSlice(reader.ReadLength("LOD child length"), "LOD child payload");
            // Attribute chunks may precede VERTICES, so vertex-dependent checks occur after this loop.
            switch (header)
            {
                case "VERTICES":
                    ValidateFixedWidthPayload(child, count, 12, "VERTICES");
                    vertexCount = count;
                    seenVertices = true;
                    break;
                case "NORMALS":
                    ValidateFixedWidthPayload(child, count, 16, "NORMALS");
                    normalCount = count;
                    seenNormals = true;
                    break;
                case "TANGENTS":
                    ValidateFixedWidthPayload(child, count, 12, "TANGENTS");
                    tangentCount = count;
                    seenTangents = true;
                    break;
                case "TEXCOORDS":
                    texCoordVertexCounts = InspectTexCoords(child, count);
                    seenTexCoords = true;
                    break;
                case "INDICES":
                    ValidateFixedWidthPayload(child, count, 4, "INDICES");
                    indices = ReadIndices(child, count);
                    seenIndices = true;
                    break;
                case "VERTEXCOLORS":
                    vertexColorCounts = InspectVertexColors(child, count);
                    break;
                case "MATERIALS":
                    materialsPayload = child;
                    materialCount = count;
                    seenMaterials = true;
                    break;
                case "WEIGHTS":
                    weightVertexIndices = InspectWeights(child, count);
                    break;
                case "MORPHTARGETS":
                    morphVertexIndices = InspectMorphTargets(child, count);
                    break;
                default:
                    throw Invalid($"Unsupported standard LOD child chunk: {header}.");
            }
        }
        if (!seenVertices) throw Invalid("Standard LOD is missing required VERTICES child chunk.");
        if (!seenNormals) throw Invalid("Standard LOD is missing required NORMALS child chunk.");
        if (!seenTangents) throw Invalid("Standard LOD is missing required TANGENTS child chunk.");
        if (!seenTexCoords) throw Invalid("Standard LOD is missing required TEXCOORDS child chunk.");
        if (!seenIndices) throw Invalid("Standard LOD is missing required INDICES child chunk.");
        if (!seenMaterials) throw Invalid("Standard LOD is missing required MATERIALS child chunk.");
        if (normalCount is not null && normalCount != vertexCount) throw Invalid("NORMALS count must match VERTICES.");
        if (tangentCount is not null && tangentCount != vertexCount) throw Invalid("TANGENTS count must match VERTICES.");
        ValidateVertexCounts(texCoordVertexCounts, vertexCount, "TEXCOORDS");
        ValidateVertexCounts(vertexColorCounts, vertexCount, "VERTEXCOLORS");
        ValidateVertexIndices(weightVertexIndices, vertexCount, "WEIGHTS");
        ValidateVertexIndices(morphVertexIndices, vertexCount, "MORPHTARGETS");
        var slots = InspectSections(materialsPayload, materialCount, indices.Length);
        hasGeometry = vertexCount > 0 && indices.Length > 0;
        return new InspectedLodTopology(lodIndex, vertexCount, indices.Length, indices, slots);
    }

    private static int[] InspectTexCoords(ReadOnlySpan<byte> payload, int count)
    {
        var reader = new Reader(payload);
        var vertexCounts = new int[count];
        for (var index = 0; index < count; index++)
        {
            var vertexCount = reader.ReadCount("TEXCOORDS vertex count");
            _ = reader.ReadBytes(CheckedByteLength(vertexCount, 8, "TEXCOORDS"), "TEXCOORDS values");
            vertexCounts[index] = vertexCount;
        }
        if (!reader.End) throw Invalid("Unread trailing bytes in TEXCOORDS chunk.");
        return vertexCounts;
    }

    private static int[] InspectVertexColors(ReadOnlySpan<byte> payload, int count)
    {
        var reader = new Reader(payload);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var vertexCounts = new int[count];
        for (var index = 0; index < count; index++)
        {
            var name = reader.ReadString("VERTEXCOLORS set name");
            if (!names.Add(name)) throw Invalid("VERTEXCOLORS set names must be unique.");
            var vertexCount = reader.ReadCount("VERTEXCOLORS vertex count");
            _ = reader.ReadBytes(CheckedByteLength(vertexCount, 4, "VERTEXCOLORS"), "VERTEXCOLORS values");
            vertexCounts[index] = vertexCount;
        }
        if (!reader.End) throw Invalid("Unread trailing bytes in VERTEXCOLORS chunk.");
        return vertexCounts;
    }

    private static int[] InspectWeights(ReadOnlySpan<byte> payload, int count)
    {
        ValidateFixedWidthPayload(payload, count, 10, "WEIGHTS");
        var reader = new Reader(payload);
        var vertexIndices = new int[count];
        for (var index = 0; index < count; index++)
        {
            _ = reader.ReadUInt16("WEIGHTS bone index");
            vertexIndices[index] = reader.ReadInt32("WEIGHTS vertex index");
            _ = reader.ReadBytes(4, "WEIGHTS weight");
        }
        return vertexIndices;
    }

    private static int[] InspectMorphTargets(ReadOnlySpan<byte> payload, int count)
    {
        var reader = new Reader(payload);
        var vertexIndices = new List<int>();
        for (var morphIndex = 0; morphIndex < count; morphIndex++)
        {
            _ = reader.ReadString("MORPHTARGETS name");
            var deltaCount = reader.ReadCount("MORPHTARGETS delta count");
            for (var deltaIndex = 0; deltaIndex < deltaCount; deltaIndex++)
            {
                _ = reader.ReadBytes(24, "MORPHTARGETS delta vectors");
                var vertexIndex = reader.ReadUInt32("MORPHTARGETS vertex index");
                if (vertexIndex > int.MaxValue) throw Invalid("MORPHTARGETS vertex index exceeds supported range.");
                vertexIndices.Add((int)vertexIndex);
            }
        }
        if (!reader.End) throw Invalid("Unread trailing bytes in MORPHTARGETS chunk.");
        return vertexIndices.ToArray();
    }

    private static int CheckedByteLength(int count, int elementSize, string description)
    {
        if (count > int.MaxValue / elementSize) throw Invalid($"Invalid {description} payload length.");
        return count * elementSize;
    }

    private static void ValidateVertexCounts(int[]? counts, int vertexCount, string description)
    {
        if (counts is not null && counts.Any(count => count != vertexCount))
            throw Invalid($"{description} vertex count must match VERTICES.");
    }

    private static void ValidateVertexIndices(int[]? indices, int vertexCount, string description)
    {
        if (indices is not null && indices.Any(index => index < 0 || index >= vertexCount))
            throw Invalid($"{description} vertex index is outside VERTICES.");
    }

    private static void ValidateFixedWidthPayload(ReadOnlySpan<byte> payload, int count, int elementSize, string description)
    {
        if (count > int.MaxValue / elementSize || payload.Length != count * elementSize)
            throw Invalid($"Invalid {description} payload length.");
    }

    private static int[] ReadIndices(ReadOnlySpan<byte> payload, int count)
    {
        var indices = new int[count];
        for (var index = 0; index < count; index++)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(index * 4, 4));
            if (value > int.MaxValue) throw Invalid("INDICES value exceeds supported range.");
            indices[index] = (int)value;
        }
        return indices;
    }

    private static ImmutableArray<InspectedLodMaterialSlot> InspectSections(ReadOnlySpan<byte> payload, int count, int indexCount)
    {
        var reader = new Reader(payload);
        var slots = ImmutableArray.CreateBuilder<InspectedLodMaterialSlot>(count);
        for (var index = 0; index < count; index++)
        {
            _ = reader.ReadString("material name");
            var materialPath = reader.ReadString("material path");
            var firstIndex = reader.ReadUInt32("material first index");
            var faceCount = reader.ReadUInt32("material face count");
            if (firstIndex % 3 != 0 || faceCount > (uint)(int.MaxValue / 3) || firstIndex > (uint)indexCount || faceCount * 3 > (uint)indexCount - firstIndex)
                throw Invalid("Standard LOD MATERIALS range is outside INDICES.");
            slots.Add(new InspectedLodMaterialSlot(index, materialPath));
        }
        if (!reader.End) throw Invalid("Unread trailing bytes in MATERIALS chunk.");
        return slots.ToImmutable();
    }

    private static InspectedSkeletonTopology InspectSkeleton(ReadOnlySpan<byte> payload, int count)
    {
        if (count != 1) throw Invalid("SKELETON envelope count must be exactly 1.");
        var reader = new Reader(payload);
        string? skeletonPath = null;
        ImmutableArray<InspectedSkeletonBone>? bones = null;
        var seenHeaders = new HashSet<string>(StringComparer.Ordinal);
        while (!reader.End)
        {
            var header = reader.ReadString("SKELETON child header");
            if (!seenHeaders.Add(header)) throw Invalid("Duplicate SKELETON child chunk.");
            var childCount = reader.ReadCount("SKELETON child count");
            var child = reader.ReadSlice(reader.ReadLength("SKELETON child length"), "SKELETON child payload");
            switch (header)
            {
                case "METADATA":
                    if (childCount != 1) throw Invalid("SKELETON METADATA count must be exactly 1.");
                    var metadata = new Reader(child);
                    skeletonPath = metadata.ReadString("SKELETON metadata path");
                    if (!metadata.End) throw Invalid("Unread trailing bytes in SKELETON METADATA chunk.");
                    break;
                case "BONES":
                    bones = InspectBones(child, childCount);
                    break;
                case "SOCKETS":
                    ValidateSockets(child, childCount);
                    break;
                case "VIRTUALBONES":
                    ValidateVirtualBones(child, childCount);
                    break;
                default:
                    throw Invalid($"Unsupported SKELETON child chunk: {header}.");
            }
        }
        if (skeletonPath is null || bones is null) throw Invalid("SKELETON requires METADATA and BONES chunks.");
        try { LogicalUnrealPath.Validate(skeletonPath, nameof(skeletonPath)); }
        catch (ArgumentException exception) { throw Invalid($"Invalid SKELETON metadata path: {exception.Message}"); }
        // Validate structural constraints before asking the identity helper to hash names/parents.
        var provisional = new InspectedSkeletonTopology(bones.Value, bones.Value.Length, ImmutableArray.CreateRange(new byte[32]), null);
        try { UeModelTopologyInspection.ValidateSkeletonTopologyStructure(provisional); }
        catch (InvalidDataException) { throw; }
        var hash = SkeletonIdentityV1.ComputeLayoutHash(bones.Value.Select(bone => new SkeletonBone(bone.Name, bone.ParentIndex)));
        return new InspectedSkeletonTopology(bones.Value, bones.Value.Length, hash, new SkeletonIdentityV1(skeletonPath, null, hash));
    }

    private static ImmutableArray<InspectedSkeletonBone> InspectBones(ReadOnlySpan<byte> payload, int count)
    {
        var reader = new Reader(payload);
        var bones = ImmutableArray.CreateBuilder<InspectedSkeletonBone>(count);
        for (var index = 0; index < count; index++)
        {
            var name = reader.ReadString("bone name");
            var parent = reader.ReadInt32("bone parent index");
            _ = reader.ReadBytes(12 + 16, "bone transform");
            bones.Add(new InspectedSkeletonBone(index, name, parent));
        }
        if (!reader.End) throw Invalid("Unread trailing bytes in SKELETON BONES chunk.");
        return bones.ToImmutable();
    }

    private static void ValidateSockets(ReadOnlySpan<byte> payload, int count)
    {
        var reader = new Reader(payload);
        for (var index = 0; index < count; index++)
        {
            _ = reader.ReadString("socket name"); _ = reader.ReadString("socket bone name"); _ = reader.ReadBytes(12 + 16 + 12, "socket transform");
        }
        if (!reader.End) throw Invalid("Unread trailing bytes in SKELETON SOCKETS chunk.");
    }

    private static void ValidateVirtualBones(ReadOnlySpan<byte> payload, int count)
    {
        var reader = new Reader(payload);
        for (var index = 0; index < count; index++) { _ = reader.ReadString("virtual bone source"); _ = reader.ReadString("virtual bone target"); _ = reader.ReadString("virtual bone name"); }
        if (!reader.End) throw Invalid("Unread trailing bytes in SKELETON VIRTUALBONES chunk.");
    }

    private static ImmutableArray<InspectedMaterialLink> InspectMaterialChunkCore(ReadOnlySpan<byte> payload, out int version)
    {
        var reader = new Reader(payload);
        version = reader.ReadByte("FMODEL_MATERIALS version");
        if (version != 2) throw Invalid($"Unsupported FMODEL_MATERIALS version: {version}.");
        var count = reader.ReadCount("FMODEL_MATERIALS slot count");
        var result = ImmutableArray.CreateBuilder<InspectedMaterialLink>();
        var previousSlotIndex = -1;
        for (var index = 0; index < count; index++)
        {
            var slotIndex = reader.ReadInt32("slot index");
            var sourceMaterialIndex = reader.ReadInt32("source material index");
            if (slotIndex < 0 || sourceMaterialIndex < 0) throw Invalid("Material indices must be non-negative.");
            if (slotIndex <= previousSlotIndex) throw Invalid("FMODEL_MATERIALS slot indices must be strictly ascending.");
            previousSlotIndex = slotIndex;
            var name = reader.ReadString("material name");
            var path = reader.ReadString("Unreal material path");
            var uri = reader.ReadString("material JSON URI");
            var parameterCount = reader.ReadInt32("parameter count");
            if (MaterialLinkSemanticValidation.IsBlankOrContainsNul(name))
                throw Invalid("Material name must not be blank.");
            if (MaterialLinkSemanticValidation.IsBlankOrContainsNul(path))
                throw Invalid("Unreal material path must not be blank.");
            if (!MaterialLinkSemanticValidation.IsLogicalUri(uri, allowLeadingParents: true))
                throw Invalid("Material JSON URI must be a non-rooted logical URI.");
            if (parameterCount != 0)
                throw Invalid("FMODEL_MATERIALS parameter count must be zero for version 2.");
            result.Add(new(slotIndex, sourceMaterialIndex, name, path, uri, parameterCount));
        }
        if (!reader.End) throw Invalid("Unread trailing bytes in FMODEL_MATERIALS chunk.");
        return result.ToImmutable();
    }

    private static InvalidDataException Invalid(string message) => new(message);

    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> _remaining = bytes;
        public bool End => _remaining.IsEmpty;
        public byte ReadByte(string description) => ReadBytes(1, description)[0];
        public bool ReadBoolean(string description) => ReadByte(description) switch { 0 => false, 1 => true, _ => throw Invalid($"Invalid {description}.") };
        public uint ReadUInt32(string description) => BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(4, description));
        public ushort ReadUInt16(string description) => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2, description));
        public int ReadInt32(string description) => BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(4, description));
        public int ReadCount(string description)
        {
            var value = ReadInt32(description);
            if (value < 0 || value > _remaining.Length) throw Invalid($"Invalid {description}.");
            return value;
        }
        public int ReadLength(string description)
        {
            var value = ReadInt32(description);
            if (value < 0 || value > _remaining.Length) throw Invalid($"Invalid {description}.");
            return value;
        }
        public string ReadString(string description)
        {
            var length = ReadLength($"{description} length");
            var value = ReadBytes(length, description);
            try
            {
                var text = new UTF8Encoding(false, true).GetString(value);
                if (text.IndexOf('\0') >= 0) throw Invalid($"Invalid {description} NUL character.");
                return text;
            }
            catch (DecoderFallbackException) { throw Invalid($"Invalid {description} UTF-8."); }
        }
        public ReadOnlySpan<byte> ReadSlice(int length, string description) => ReadBytes(length, description);
        public ReadOnlySpan<byte> ReadBytes(int length, string description = "bytes")
        {
            if (length < 0 || length > _remaining.Length) throw Invalid($"{description} exceeds remaining input.");
            var result = _remaining[..length];
            _remaining = _remaining[length..];
            return result;
        }
    }
}
