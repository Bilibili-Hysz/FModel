# Release and Blender Chain Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use compose:subagent (recommended) or compose:execute to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repair the audited release, protocol synchronization, Blender import, and batch-export defects while preserving all unrelated dirty worktree changes.

**Architecture:** Keep the canonical FModel/CUE4Parse and Blender importer trees as protocol sources. Synchronize the `UEFormat_Edited` aggregate and Blender ZIP from those sources, add deterministic release checks, and make scene replacement transactional for both strict and permissive imports. Each behavior change gets a focused regression before the broader suite.

**Tech Stack:** C#/.NET 10 WPF, CUE4Parse xUnit, Python 3/pytest, Blender importer stubs, PowerShell packaging checks, ZIP archives.

## Global Constraints

- Modify only files in the approved remediation scope; preserve unrelated existing dirty changes.
- Do not create git commits, push, reset, clean, or delete user files.
- Treat `Fmodel_Edited_ueformat` and `io_scene_ueformat-fmodel` as the canonical source trees.
- Keep material sidecars optional and keep required mesh/model validation fail-closed.
- Do not claim Blender runtime success without a real Blender 5.2 executable.

---

### Task 1: Add failing regressions for importer contract defects

**Covers:** S2, S4, S5

**Files:**
- Modify: `io_scene_ueformat-fmodel/tests/test_fmodel_material_links.py`
- Modify: `io_scene_ueformat-fmodel/tests/test_uescene.py`

**Interfaces:**
- Consumes: existing synthetic UEMODEL/UEScene builders, `read_uemodel_source_lods()`, `import_uescene_plan()`.
- Produces: focused tests that fail against the current compressed preflight, permissive replacement, optional material sidecar, and MATO behavior.

- [ ] **Step 1: Add a compressed UEMODEL preflight regression.** Build a minimal valid compressed UEMODEL using the repository writer/test fixture helpers, with both compression header size fields present, then assert `read_uemodel_source_lods()` returns its LOD set and material mapping.
- [ ] **Step 2: Add a permissive replacement rollback regression.** Materialize a valid existing scene, make the replacement model importer raise, call `import_uescene_plan(..., strict_required_models=False)`, and assert the old source-key collection remains while the returned summary reports the failed model.
- [ ] **Step 3: Add an optional material-sidecar regression.** Construct a valid scene with an available kind-2 material asset but no material BNDX entry; assert parsing and plan construction succeed while required mesh/model entries remain enforced.
- [ ] **Step 4: Add a MATO slot-application regression.** Use the existing Blender stub objects and a model with source material slot indices; provide an override material asset and assert the created mesh object's material slot is replaced and provenance metadata remains present.
- [ ] **Step 5: Run only the new tests and record the expected failures.**

Run:

```powershell
py -3 -m pytest -q tests/test_fmodel_material_links.py tests/test_uescene.py
```

Expected before implementation: the new compressed, permissive rollback, optional sidecar, or effective MATO assertions fail; unrelated existing tests remain green.

---

### Task 2: Fix compressed preflight and transactional permissive import

**Covers:** S2, S4, S5

**Files:**
- Modify: `io_scene_ueformat-fmodel/plugins/blender/io_scene_ueformat/importer/logic.py:80-89`
- Modify: `io_scene_ueformat-fmodel/plugins/blender/io_scene_ueformat/importer/scene_import.py:320-529`
- Modify: `io_scene_ueformat-fmodel/tests/test_fmodel_material_links.py`
- Modify: `io_scene_ueformat-fmodel/tests/test_uescene.py`

**Interfaces:**
- Consumes: `FArchiveReader`, `UEFormatImport.import_data_by_reader()`, `_snapshot_import_data()`, `_rollback_import_data()`, existing strict staging path.
- Produces: correct compressed UEMODEL preflight and a single transactional replacement path shared by strict and permissive imports.

- [ ] **Step 1: Make the compressed regression pass minimally.** Read `_compressed_size = ar.read_int()` immediately after `uncompressed_size` in `read_uemodel_source_lods()` and continue decompression from the payload after both fields.
- [ ] **Step 2: Make preflight failures visible.** In `_validate_model_lod_manifests()`, convert ordinary `OSError`/`ValueError` for a required model into `RequiredModelImportError` with a stable code such as `model_preflight_failed`, retaining the model path and original exception detail. Do not swallow malformed required model contracts.
- [ ] **Step 3: Reuse staging for permissive replacement.** Change `import_uescene_plan()` so both strict and permissive replacement calls `_materialize_uescene_plan(..., staging_root=..., replace_existing=False)`, then atomically links the staged scene. In permissive mode, allow partial model summaries to return only when the existing scene is not replaced; if the product contract requires partial replacement, keep the old collection until the staged summary is complete and explicitly mark the new collection partial.
- [ ] **Step 4: Preserve rollback semantics.** On any raised error, remove staging and call `_rollback_import_data(before, preserve_shared=True)`; on success, unlink staging, replace the old source-key collection, link the new collection, and remove staging root.
- [ ] **Step 5: Run focused importer tests until green.**

Run:

```powershell
py -3 -m pytest -q tests/test_fmodel_material_links.py tests/test_uescene.py
```

Expected: all focused tests pass, including compressed preflight and failed permissive replacement preservation.

---

### Task 3: Restore optional material sidecars and apply MATO overrides

**Covers:** S2, S4, S5

**Files:**
- Modify: `io_scene_ueformat-fmodel/plugins/blender/io_scene_ueformat/importer/uescene.py:275-300`
- Modify: `io_scene_ueformat-fmodel/plugins/blender/io_scene_ueformat/importer/scene_import.py:130-142,382-420`
- Modify: `io_scene_ueformat-fmodel/plugins/blender/io_scene_ueformat/importer/logic.py:231-380` only if a small reusable material-slot helper is required
- Modify: `io_scene_ueformat-fmodel/tests/test_uescene.py`

**Interfaces:**
- Consumes: `MaterialOverride`, `Asset`, `BundleEntry`, `FModelMaterialImportOptions`, the objects created by `UEFormatImport.import_file()`.
- Produces: optional material sidecar validation and a deterministic post-model material override application helper that maps `slot_ordinal` to source material slots.

- [ ] **Step 1: Restore required/optional classification.** Put available kind-1 mesh assets in `required`, keep available kind-2 material assets in `optional`, and retain strict BNDX matching for every entry that is present. Only required mesh/model keys must be a subset of actual BNDX keys.
- [ ] **Step 2: Add a narrow override application helper.** Implement a helper with a concrete signature such as `def _apply_material_overrides(created_objects, component, assets, bundle_root, material_options) -> None`. It must select mesh objects created for the component, map each `Material`'s `source_slot_index` to the scene `slot_ordinal`, resolve the override material sidecar through the existing bundle/material importer path, replace only the matching Blender material slot, and preserve `uescene_material_overrides` metadata.
- [ ] **Step 3: Keep parameter-only overrides honest.** If `parameter_payload` has no supported Blender parameter mapping, retain the payload in metadata and add a warning/diagnostic; do not claim effective material parameter application.
- [ ] **Step 4: Call the helper only after successful model import and LOD acceptance.** If the helper fails, roll back the component's newly created objects and report a required-model failure through the existing summary path.
- [ ] **Step 5: Run the optional-sidecar and MATO tests.**

Run:

```powershell
py -3 -m pytest -q tests/test_uescene.py tests/test_fmodel_material_links.py
```

Expected: optional material sidecar scenes are accepted, malformed required model resources remain rejected, and MATO slot replacement is asserted.

---

### Task 4: Synchronize canonical protocol sources into aggregate and ZIP

**Covers:** S3, S5

**Files:**
- Modify: `Fmodel_Edited_ueformat/CUE4Parse/CUE4Parse-Conversion/Materials/MaterialExporter2.cs`
- Modify: `Fmodel_Edited_ueformat/CUE4Parse/CUE4Parse-Conversion/UEScene/UESceneBundleExporter.cs`
- Modify: `UEFormat_Edited/Fmodel_Edited_ueformat/CUE4Parse/CUE4Parse-Conversion/Materials/MaterialExporter2.cs`
- Modify: `UEFormat_Edited/Fmodel_Edited_ueformat/CUE4Parse/CUE4Parse-Conversion/UEScene/UESceneBundleExporter.cs`
- Modify or create: `scripts/sync_release_sources.py`
- Modify or create: `tests/test_release_sources.py`
- Regenerate: `io_scene_ueformat.zip`, `dist/io_scene_ueformat.zip` only through the packaging command; exclude caches.

**Interfaces:**
- Consumes: canonical addon directory and canonical CUE4Parse exporter files.
- Produces: aggregate source parity, ZIP containing current V1/V2 parser, and a deterministic source/archive parity check.

- [ ] **Step 1: Synchronize material exporter and UEScene reconciliation logic.** Copy only the approved canonical files into the aggregate paths; preserve aggregate-only project files outside this protocol scope.
- [ ] **Step 2: Add a deterministic addon ZIP builder.** Build `io_scene_ueformat.zip` from `io_scene_ueformat-fmodel/plugins/blender/io_scene_ueformat`, include source/manifest files, exclude every `__pycache__` directory and `.pyc`, and write stable relative paths.
- [ ] **Step 3: Add source parity tests.** Verify canonical vs aggregate SHA-256 for the protocol files and verify ZIP `classes.py` accepts `(1, 2)` and contains no generated cache entries.
- [ ] **Step 4: Regenerate both checked-in/distribution ZIP locations using the builder.** Do not copy stale ZIP bytes manually.
- [ ] **Step 5: Run the parity tests and inspect the archive contents.**

Run:

```powershell
py -3 -m pytest -q tests/test_release_sources.py
```

Expected: canonical/aggregate hashes match, both generated ZIPs are cache-free, and the packaged parser accepts V1/V2 material extensions.

---

### Task 5: Fix release workflow and package verification

**Covers:** S3, S5

**Files:**
- Modify: `Fmodel_Edited_ueformat/.github/workflows/main.yml:33`
- Modify: `package_release.py`
- Modify: `UEFormat_Edited/package_release.py`
- Modify: existing release behavior tests embedded in the package scripts or extract only if the repository pattern requires it

**Interfaces:**
- Consumes: `FModel.csproj`, canonical/aggregate parity checker, `scripts/sync_release_sources.py`.
- Produces: clean CI publish target and release script that refuses stale protocol sources before replacing `FModel.exe`.

- [ ] **Step 1: Change the workflow publish framework from `net8.0-windows` to `net10.0-windows`.** Keep the existing runtime and publish flags unchanged.
- [ ] **Step 2: Add a non-destructive preflight to package scripts.** Before `dotnet publish`, verify the expected project target, canonical/aggregate protocol parity, and addon ZIP V1/V2 support. Return a nonzero status with an actionable path when checks fail.
- [ ] **Step 3: Preserve executable-only replacement semantics.** Do not delete existing exports/logs; keep the existing behavior tests for success, publish failure, and locked executable.
- [ ] **Step 4: Extend release behavior tests to cover failed parity preflight and current target framework.**
- [ ] **Step 5: Run package behavior and syntax checks.**

Run:

```powershell
py -3 package_release.py --behavior-test
py -3 -m py_compile package_release.py UEFormat_Edited/package_release.py scripts/sync_release_sources.py
```

Expected: all behavior cases pass and a stale aggregate/ZIP is rejected before publish.

---

### Task 6: Isolate bulk UEScene export failures

**Covers:** S2, S5

**Files:**
- Modify: `Fmodel_Edited_ueformat/FModel/ViewModels/Commands/RightClickMenuCommand.cs:83-92`
- Modify: `Fmodel_Edited_ueformat/FModel/ViewModels/ThreadWorkerViewModel.cs` only if the existing failure-count API cannot be used locally
- Add or modify: the closest existing FModel command test project for a one-failing-map/one-successful-map regression

**Interfaces:**
- Consumes: `SaveUESceneBundle`, `ExportedCount`, `FailedExportCount`, worker cancellation token.
- Produces: per-entry try/catch that logs the failed map, increments failure count, continues to later maps, and still propagates cancellation.

- [ ] **Step 1: Add a command-level regression with two selected maps.** Arrange the first export to throw and the second to succeed; assert the second is invoked and `FailedExportCount` is incremented once.
- [ ] **Step 2: Wrap only one entry in `try/catch (Exception)` inside the UEScene loop.** Keep `OperationCanceledException` flowing to the outer worker so cancellation remains correct; log map identity and continue after ordinary failures.
- [ ] **Step 3: Verify the command change.** This checkout has no dedicated FModel command-test project. First run the FModel solution build/test target if it becomes available; otherwise run a compile/build of `FModel/FModel.slnx` and inspect the loop's per-entry control flow as the available regression evidence. Do not invent a nonexistent test command.

Run:

```powershell
dotnet build FModel/FModel.slnx --no-restore --verbosity minimal
```

Expected: the FModel solution compiles, the loop catches ordinary per-map failures, and cancellation still propagates.

---

### Task 7: Full verification and handoff report

**Covers:** S5, S6

**Files:**
- No production files by default.
- Optional: `docs/compose/reports/2026-07-22-release-blender-chain-remediation-report.md` if the final report is saved.

**Interfaces:**
- Consumes: all prior task changes and generated artifacts.
- Produces: evidence-backed verification report and explicit clean-checkout/gitlink/runtime gaps.

- [ ] **Step 1: Run focused Python regressions.**

```powershell
py -3 -m pytest -q tests/test_fmodel_material_links.py tests/test_uescene.py tests/test_release_sources.py
```

- [ ] **Step 2: Run the full Blender importer suite.**

```powershell
py -3 -m pytest -q
```

- [ ] **Step 3: Run CUE4Parse focused and full tests.**

```powershell
dotnet test CUE4Parse/CUE4Parse.Tests/CUE4Parse.Tests.csproj --no-restore --verbosity minimal
```

If a DLL lock occurs, do not kill unrelated processes; rerun the already-built assembly with `--no-build` and report both outcomes.

- [ ] **Step 4: Run release behavior, syntax, parity, and archive-content checks.**
- [ ] **Step 5: Check source/aggregate/ZIP/deployed hashes and artifact timestamps.**
- [ ] **Step 6: If Blender 5.2 executable is absent, report runtime import as unverified rather than passing it by inference.**
- [ ] **Step 7: Report that CUE4Parse changes still require a submodule commit and parent gitlink update before clean CI can consume them.**

---

## Plan self-review

- Spec coverage: S2 is covered by Tasks 1, 2, 3, and 6; S3 by Tasks 4 and 5; S4 by Tasks 1–3; S5 by every implementation task and Task 7; S6 by Task 7.
- Placeholder scan: no `TBD`, `TODO`, unresolved path token, or unspecified “add tests” step remains; Task 6 explicitly uses the available FModel solution build when no command-test project exists.
- Type consistency: compressed preflight uses the existing `read_uemodel_source_lods()` contract; scene staging uses existing snapshot/rollback helpers; release parity helper is shared by package scripts and tests.
