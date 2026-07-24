# Release and Blender Chain Remediation Design

## [S1] Goal

修复全局审计确认的发布、协议同步、Blender 导入和批量导出问题，使 canonical source、`UEFormat_Edited` 聚合副本、Blender ZIP 与部署 addon 使用同一套 UEFormat V1/V2 合同，并让失败导入不会静默破坏已有场景。

本轮只修改工作区文件，不自动创建 git commit；CUE4Parse 子模块的最终 commit 与父仓库 gitlink 更新作为交付后的 VCS 集成步骤单独报告。

## [S2] Scope and invariants

- Release workflow 必须使用 `FModel.csproj` 实际声明的 `net10.0-windows`。
- 压缩 UEMODEL 的所有读取路径必须消费 `uncompressed_size` 和 `compressed_size` 头字段后再解压。
- 普通 preflight 失败不能被静默丢弃；必须形成可诊断的 required-model failure，且不在创建 Blender 图之前破坏旧图。
- replace-style scene import 在 strict 和 permissive 两种模式下都必须先 staging，只有完整 materialization 成功后才替换旧 collection。
- material sidecar 保持 optional 语义；required BNDX 只包含可用的 mesh/model semantic resources，material sidecar 只有在 BNDX 已声明时才校验。
- `FMODEL_MATERIALS` V1/V2、`SourceMaterialIndex` 与 canonical Unreal material identity 必须在 canonical exporter、aggregate source、Blender source、ZIP 和 managed deployment 中一致。
- 批量 UEScene export 必须逐条隔离异常，单个 map 失败不能终止后续 map。
- 所有新行为必须先有 focused regression，再运行全量测试；不得把没有 Blender executable 的静态测试描述为真实 Blender runtime 成功。

## [S3] Canonical-to-release synchronization

canonical FModel/CUE4Parse 与 Blender importer 是协议源。`UEFormat_Edited` 只能从 canonical 内容同步生成，Blender ZIP 只能从 canonical addon 目录重新打包，并排除 `__pycache__`/`.pyc`。发布脚本必须在复制或发布前检查协议版本和关键源码 hash，防止旧 aggregate 或 ZIP 被当作当前 release。

## [S4] Blender importer behavior

`read_uemodel_source_lods()` 使用与正常 `UEFormatImport.import_data_by_reader()` 相同的压缩头解析。`import_uescene_plan()` 在 preflight、model import 或 requested LOD 失败时保留旧 scene graph；permissive 模式可以返回 partial summary，但 replacement 必须在 staging collection 内完成。

MATO 的现有协议校验和 provenance 保留继续保留；同时在模型 material slots 已创建后，将有明确 `override_material_asset_id` 的 override 应用到对应 source material slot。parameter-only provenance 在缺少可执行参数映射时保留为可诊断 metadata，不伪称为已渲染应用。

## [S5] Verification contract

- Blender Python suite：focused regressions pass，随后全量 `pytest -q` pass；Windows symlink skips 可保留并报告。
- CUE4Parse suite：focused regressions pass，随后 `dotnet test` pass；必须使用 clean/build 或 `--no-build` 的明确证据，锁冲突不得被当成测试结果。
- Release behavior test 和 Python compilation pass。
- aggregate 与 canonical 关键文件 hash/协议检查 pass，ZIP 无 generated cache 且接受 V1/V2。
- 若无 Blender 5.2 executable，报告必须保留 runtime smoke validation gap。

## [S6] Out of scope

- 不在本轮处理无关 UI 重构、依赖升级或历史 cache 清理。
- 不强行修改或删除用户未提交的其他文件。
- 不自动创建 commit、push 或修改远端状态。
