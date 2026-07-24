# FModel Edited — Unreal Engine Archives Explorer

An edited FModel Fork focused on reliable Unreal Engine archive inspection,
FModel-to-UEFormat export, and Blender interoperability.

This repository is based on [4sval/FModel](https://github.com/4sval/FModel).
The edited branch keeps the upstream application while adding and validating
the export workflow used by [UEFormat](https://github.com/hysz-01/UEFormat)
and its Blender importer.
------------------------------------------

[![CI Status](https://img.shields.io/github/actions/workflow/status/hysz-01/FModel/qa.yml?label=CI)](https://github.com/hysz-01/FModel/actions)
[![Latest](https://img.shields.io/github/v/release/hysz-01/FModel?color=yellow)](https://github.com/hysz-01/FModel/releases)
[![Donate](https://img.shields.io/badge/sponsor-DB61A2?logo=GitHub-Sponsors&logoColor=white)](https://fmodel.app/donate)
[![Discord](https://discord.com/api/guilds/637265123144237061/widget.png?style=shield)](https://fmodel.app/discord)
***

## Edited pipeline

```text
Unreal Engine archives → FModel / CUE4Parse → UEFormat exports → Blender
```

The edited branch focuses on:

- UEScene export eligibility, structured publication outcomes, and strict resource validation.
- Material and texture URI export for reliable UEFormat and Blender resource lookup.
- Archive startup batching and game-directory scheduling reliability.
- CUE4Parse integration for UEMODEL, UEANIM, UEScene, material, and texture workflows.
- Release and test coverage for the FModel-to-Blender asset pipeline.

FModel remains an archive explorer for [Unreal Engine](https://www.unrealengine.com/en-US/)
games using [CUE4Parse](https://github.com/FabianFG/CUE4Parse) as its core parsing
library. Upstream application functionality and community attribution remain
preserved.

FModel is actively maintained and developed by a dedicated community of contributors, and welcomes all new contributions and feedback.

### Installation:
For installation, follow the instructions from [here](https://github.com/4sval/FModel/wiki/Installing-FModel).
Edited Windows builds are published in the [FModel Edited Releases](https://github.com/hysz-01/FModel/releases).

### Development and verification:

Build the application with the .NET SDK and run the CUE4Parse tests from the
`CUE4Parse` directory:

```powershell
dotnet test CUE4Parse.Tests/CUE4Parse.Tests.csproj
```

The companion [UEFormat Fork](https://github.com/hysz-01/UEFormat) contains the
Blender importer, fixtures, and cross-pipeline validation.

### Sponsorship:
<p>
  <a href="https://www.jetbrains.com/">
    <img src="https://cdn.fmodel.app/i/svg/jetbrains.svg" width="256px">
  </a>
  <a href="https://1password.com/">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="https://cdn.fmodel.app/i/svg/1password-light.svg">
      <source media="(prefers-color-scheme: light)" srcset="https://cdn.fmodel.app/i/svg/1password-dark.svg">
      <img src="https://cdn.fmodel.app/i/svg/1password-light.svg" width="256px">
    </picture>
  </a>
</p>

### License:
FModel is licensed under [GPL-3](https://github.com/4sval/FModel/blob/dev/LICENSE), and licenses of third-party libraries used are listed [here](https://github.com/4sval/FModel/blob/dev/NOTICE).
