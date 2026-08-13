# CUE4Parse.FModelUEFormat.ExportPipeline

This is the V3 adapter boundary for the FModel `aug-2026` / CUE4Parse `ExportSession` pipeline.

The project remains buildable without an upstream checkout: upstream-dependent exporters, formats, material projection, URI resolution, and texture preview sources are excluded unless `NewPipelineCUE4ParseRoot` is supplied or the disposable composition layout is detected automatically. Inside a composition, it references the exact `CUE4Parse` and `CUE4Parse-Conversion` projects and returns upstream `ExportFile` values instead of writing files directly.

The adapter owns enabled top-level `UStaticMesh` and `USkeletalMesh` requests. Geometry conversion, morph-target preparation, DNA dependency queueing, material/texture queueing, output path resolution, and disk writes remain upstream-owned. Standard UEFormat bytes are preserved; validated `FMODEL_MATERIALS` and `FMODEL_TEXTURES` chunks are appended only after upstream serialization. Linked mode preserves standard output on optional extension failure and emits exporter-context diagnostics; strict mode turns incomplete projection into a failed `ExportResult`. Skeletal components inside `.uescene` remain outside the current UEScene V1 component protocol and are not represented as static meshes.

The protocol core remains in `../CUE4Parse.FModelUEFormat`. This project must not reference FModel, WPF, application settings, or protected `vendor`/`staging` paths.
