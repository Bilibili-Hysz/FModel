# FModel And Blender UEFormat Audit

Date: 2026-07-18

## Scope

This review covers the current working trees for FModel, CUE4Parse conversion code,
and the Blender UEFormat add-on. It focuses on normal material-link exports and
UEScene bundle exports/imports. The actual `house_photo001.uescene` bundle was used
for Blender import verification.

## Overall Assessment

The architecture has a solid separation of responsibilities:

- FModel selects producer policy and invokes scene-level export.
- CUE4Parse owns canonical resource planning, binary serialization, integrity checks,
  staging, promotion, and rollback.
- Blender parses and validates a bpy-free UEScene plan before mutation.
- Blender keeps collection grouping, material construction, and hierarchy flattening
  separate from wire parsing.

The scene publication flow is particularly well designed: resources are staged and
reread, model and sidecar hashes are incorporated into the final document, the
serialized scene is reread before publication, and the scene is promoted last.

The material-link design is also directionally correct. It carries logical resource
location, source identity, texture metadata, and PBR role information rather than
requiring Blender to infer meaning from a filename. Relative, Bundle, and Embedded
remain distinct resolution modes; producer layout is not treated as wire semantics.

## Findings

### P1: ResourceManifest silently accepts conflicting definitions

`CUE4Parse-Conversion/UEFormat/ResourceManifest.cs:16-26`

`AddOrGet` deduplicates exclusively by `unrealObjectPath`. A later declaration for
the same identity can provide different URI, MIME, bytes, external flag, or physical
target, but the method returns the original record without comparing those fields.
The caller then publishes the first declaration. This is a data-integrity failure for
normal material/texture export whenever two producer paths disagree about the same
resource definition.

Required correction: reject non-identical redefinitions at this shared manifest
boundary. A duplicate should be accepted only when every serialized and publication
relevant field is equal.

### P2: The normative UEScene wire specification still declares removed CAMR support

`docs/uescene-v1-wire.md:67-71`

The specification says `CAMR` and optional feature bit 2 are part of V1. Current
C# and Blender code deliberately removed `CameraRecord`, `CameraRecordsBit`, CAMR
read/write support, camera diagnostics, and Blender camera materialization. A future
producer following the document can therefore emit a scene that the current Blender
parser treats as an unknown optional chunk and does not materialize as documented.

Required correction: remove CAMR and CAMERA_RECORDS from the V1 specification, or
restore a complete tested cross-language implementation. The current product decision
is removal, so the document should be amended accordingly.

## Material Export Pipeline Review

### Sound design decisions

- `MaterialLinkMode` distinguishes Relative, Bundle, Embedded, and disabled output.
- `MaterialLinkBuilder.Normalize` rejects rooted paths, backslashes, NUL, empty path
  segments, and traversal segments before emitting a logical resource URI.
- Bundle mode requires an absolute physical bundle root.
- Categorized layouts preserve human-readable names while appending a stable hash of
  canonical identity to avoid basename collisions.
- Blender material resolution keeps logical validation separate from physical extended
  Windows path use. That is necessary for long self-contained export paths without
  weakening BNDX/hash validation.
- Blender material import is optional and isolated from geometry import; missing or
  invalid optional material JSON can be reported without corrupting mesh geometry.

### Remaining constraints

- Material JSON sidecars are intentionally optional in UEScene. A missing material
  sidecar becomes an unavailable material asset plus diagnostic; texture sidecars are
  fail-closed. This is a reasonable boundary, but the UI/report should continue to
  surface it prominently because a successful scene import can have incomplete
  material appearance.
- PBR role selection remains a compatibility heuristic over source parameter names.
  It is appropriate as metadata/provenance, but must not be presented as a guaranteed
  lossless reconstruction of every game material graph.
- The shared manifest conflict issue above is the material pipeline's primary release
  blocker because it invalidates the assumed identity-to-bytes invariant.

## UEScene Pipeline Review

### Sound design decisions

- The V1 parser checks framing, CRC, bounds, semantic references, ownership ranges,
  URI safety, and BNDX consistency before Blender mutation.
- Static meshes use one multi-LOD primary Model BNDX resource per asset. Per-LOD
  model sidecars are not used, avoiding divergent resource identity.
- UEScene publication stages bytes below a controlled root, verifies staged content,
  rebuilds ASST/BNDX facts from staged bytes, and publishes the scene last.
- Reuse-game-export-tree mode avoids overwriting incompatible existing sidecars by
  accepting only byte-identical files.
- Unsupported components, deferred instancing, material-sidecar omissions, and world
  partition limitations are represented as diagnostics instead of silent success.

### Blender import observations

- Import collections use `<map>_uescene` with Models and Lights child collections.
  Imported model objects are now moved out of Blender's active scene collection so
  they do not appear outside the UEScene tree.
- Default hierarchy flattening is optional and enabled by default. It snapshots world
  matrices after dependency-graph evaluation, removes safe UEScene Empty nodes, then
  restores survivor world matrices. The actual House bundle retained nonzero model
  positions after flattening.
- The flattening operation deliberately preserves Empty nodes with constraints,
  animation, instanced collections, or user custom properties. That avoids deleting
  user-authored behavior at the cost of occasionally retaining a hierarchy node.

## Verification Evidence

- Focused Python UEScene tests: `74 passed, 1 skipped`.
- Focused C# UEScene format/exporter tests: `142 passed, 1 skipped`.
- Blender 5.2 smoke tests passed for UEScene static import, hierarchy flattening, and
  material/options UI.
- Actual deployed House bundle imported 47 actors, 8 static mesh components/models,
  and 4 lights. Default flattening removed 55 safe UEScene hierarchy Empty nodes.
- Actual nonzero instances of `girl005b_02_ma_wj006_01_sm_LOD0` retained their world
  coordinates after flattening: `(-2.362, 1.903, 0)`, `(2.687, 2.244, 0)`, and
  `(2.075, -3.465, 0)`.

## Recommended Follow-up Order

1. Make `ResourceManifest.AddOrGet` reject conflicting definitions and add a test for
   URI, MIME, bytes, mode, and target-path conflicts.
2. Update `uescene-v1-wire.md` to remove CAMR/CameraRecordsBit references.
3. Add a cross-process regression that imports the real House bundle twice: once with
   flatten disabled and once enabled, then compares every imported mesh/light world
   matrix within tolerance.
