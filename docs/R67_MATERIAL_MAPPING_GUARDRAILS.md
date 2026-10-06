# R67 material mapping guardrails

The frozen V10 attributes still support BaseColor, Normal and RGB ORM only.
The consumer binds ORM G to roughness and B to metallic. Upstream SpecularMasks
names include other layouts; selecting a packed texture does not prove ORM.

## Producer behavior

- Preserve exact upstream primary-name priority, normalized aliases and deterministic
  ties. An unavailable primary is not replaced by an unrelated working texture.
- For several eligible candidates, report the selected parameter, its rank and at
  most four ranked names in an information diagnostic. Single candidates stay quiet.
- Only normalized declarations ORM, Base_ORM, OcclusionRoughnessMetallicTexture and
  RGB[AO/R/Metallic]_Texture permit automatic ORM binding. Names are declarations,
  not proof of pixel or game shader semantics.
- Other selected masks produce texture.orm.layout-unverified with slot, parameter
  and recovery. Linked skips that mask while retaining valid BaseColor/Normal and
  material links. Strict fails before extension serialization. Extra preview
  encoding for the rejected selected mask is not queued; another candidate is not
  substituted. The official material/texture exporter remains authoritative.
- Disabled materials or disabled texture links do not enforce this layout guard.
- UEFormat meshes outside StaticMesh, SkeletalMesh and GeometryCollection continue
  through the official exporter with material.type.standard-only. Its warning
  explicitly says FModel attributes and Strict completeness do not cover the type.
  Skeleton-only, disabled-material and other-format exports stay quiet.

## Review gates

FModelUeModelExtensionHarness locks SHA-256 of the three upstream primary-name
arrays. Upstream alias/order changes require reviewing the selection regressions
before accepting new expected digests. This gate runs in tests, never at startup.
The harness covers Base_ORM/Base_SRO/ORM ordering, reversed inputs, safe partial
Linked serialization, Strict refusal, unknown-layout diagnostic precedence,
preview suppression, no substitution and standard-only eligibility exceptions.

No wire version, MapKind, URI, envelope or skeleton identity change is involved.
Some game materials previously treated as ORM will now keep default roughness or
metallic until a verified game-specific conversion is implemented. Real-asset
Blender/shader visual comparison remains required; synthetic tests do not replace it.
