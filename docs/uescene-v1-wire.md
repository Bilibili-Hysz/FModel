# UEScene V1 Wire Format: LITE Extension

`LITE` is an optional V1 chunk for semantic light records. It does not add or alter `ComponentKind` values and does not imply UWorld extraction.

## Feature and Presence

`SceneMetadata.LightRecordsBit` is optional-features bit 1 (`1 << 1`). Writers set it exactly when the nonempty `LITE` chunk is emitted. Readers accept files without `LITE`; otherwise the bit and the chunk must agree exactly.

The chunk version is 1, flags are zero, and the directory record count equals the payload count.

## Payload

All values are little-endian. The payload starts with `uint32 light_count`, followed by exactly `light_count` fixed 88-byte records. There is no variable payload.

| Offset | Type | Field |
|---:|---|---|
| 0 | u64 | light_id |
| 8 | u64 | actor_id |
| 16 | u64 | parent_component_id; zero means none |
| 24 | u64 | transform_id |
| 32 | u32 | name_str |
| 36 | u32 | class_path_str |
| 40 | u32 | source_object_path_str |
| 44 | u8 | light_type: Directional=1, Point=2, Spot=3, Rect=4 |
| 45 | u8 | color_encoding: RGBA8=1 |
| 46 | u8 | light_units: Unitless=0, Candelas=1, Lumens=2, EV=3, Lux=4 |
| 47 | u8 | reserved, zero |
| 48 | u8[4] | raw FColor `R,G,B,A` |
| 52 | u32 | flags |
| 56 | f32 | raw intensity |
| 60 | f32 | attenuation_radius |
| 64 | f32 | falloff_exponent |
| 68 | f32 | inner_cone_angle_degrees |
| 72 | f32 | outer_cone_angle_degrees |
| 76 | f32 | rect_width |
| 80 | f32 | rect_height |
| 84 | u32 | reserved, zero |

Flags: `CASTS_SHADOWS=1`, `HAS_ATTENUATION=2`, `HAS_SPOT_ANGLES=4`, `HAS_RECT_DIMENSIONS=8`, `USES_INVERSE_SQUARED_FALLOFF=16`. No other bits are valid.

## Strict Validity and Canonical Form

`light_id` values are nonzero and unique. Actor and transform IDs must resolve; parent component is zero or resolves. All strings resolve through `STRS`, are non-null UTF-8 strings without NUL, and source paths obey the normal UEScene source-path limit.

All numeric physical fields are finite and nonnegative. `HAS_ATTENUATION` is exact for Point and Spot. `HAS_SPOT_ANGLES` is exact for Spot, with inner <= outer. `HAS_RECT_DIMENSIONS` is exact for Rect. Directional cannot use inverse-squared falloff. Unused parameter families are exact zero; inverse-squared attenuation requires zero falloff exponent.

Writers remap actor, component, and transform references alongside canonical semantic IDs, normalize negative zero, zero all reserved bytes, and sort records by canonical actor order, parent component order, transform order, then light ID. `light_id` is a stable supplied identity and is not source-derived.
