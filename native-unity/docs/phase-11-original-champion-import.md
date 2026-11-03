# Phase 11 — external original champion assets and Windows Player staging

## Status

**Source and pipeline implementation** (not acquisition of Riot artwork).
The Git repository includes only the original skinned Garen geometry, texture and
animation buffers. The three additional champion files do **not** exist in Git:
**Blitzcrank, Riven, Yasuo**. Their existing gameplay/skill logic and tactical
markers are not substitutes for original hero meshes. The importer never
downloads or invents champion data.

The next step that needs separately supplied source material is converting each
authorized, provenance-checked champion's actual original `.lmesh`, corresponding
`.lanim` and `.png`. Do not publish source files, ZIPs or Windows Player builds
containing unlicensed original artwork.

## Persistent runner layout

On the self-hosted Windows Runner, use a directory **outside the Git checkout**:

```text
F:\actions-runner\HextechChampionSources\
  Blitzcrank\
    blitzcrank.json
    blitzcrank.bin
    original_texture_name.png
    local-import-provenance.json
    animation-bindings.json             # optional, manually source-verified
  Riven\
    ...
  Yasuo\
    ...
```

The fallback root is `F:\actions-runner\HextechChampionSources`. To use a
different root, configure the GitHub Actions **repository variable**
`HEXTECH_CHAMPION_ASSET_ROOT`, not a secret. The Windows Runner account needs
read access to this directory. It is deliberately not inside the Git working
tree: the CI workflow runs `git clean -ffdx` before each build.

### Step 1 — locally import an original rig

For each hero, find its actual source directory and actual numeric model stem;
do not guess either. Example commands (replace both example source paths and
stem IDs with verified originals):

```powershell
py -3 native-unity/tools/import_native_hero.py --hero Blitzcrank --source "D:\owned-model-sources\Blitzcrank" --stem "<verified-stem>" --output "F:\actions-runner\HextechChampionSources\Blitzcrank"
py -3 native-unity/tools/import_native_hero.py --hero Riven --source "D:\owned-model-sources\Riven" --stem "<verified-stem>" --output "F:\actions-runner\HextechChampionSources\Riven"
py -3 native-unity/tools/import_native_hero.py --hero Yasuo --source "D:\owned-model-sources\Yasuo" --stem "<verified-stem>" --output "F:\actions-runner\HextechChampionSources\Yasuo"
```

Supply an optional `--bindings path/to/animation-bindings.json` only after
checking **exact original clip names** against the converter's output. The
mapping must set `sourceVerified: true` and the actual hero name. The importer
does not infer `spell1 = Q` or otherwise invent ability animations. `attack1`,
`idle` or `idle1`, and `run` are required for a playable skinned actor.

The importer records SHA-256 hashes for each converted JSON, binary, texture
and optional binding. An older converted bundle without these hashes must be
re-imported once. Any changed source or animation mapping requires re-import
to refresh the proof.

### Step 2 — preflight and stage for a local Unity Player

```powershell
py -3 native-unity/tools/stage_native_champions.py --source-root "F:\actions-runner\HextechChampionSources" --require Blitzcrank --require Riven --require Yasuo
```

This prevalidates all present source bundles, verifies hashes and clip names,
removes stale ability mappings, copies into the ignored
`native-unity/Assets/StreamingAssets/Champions/<Hero>` folders on the same
filesystem, and commits each folder by rename. It never touches the already
verified Garen baseline. Missing optional sources are allowed when
`--require` is omitted; explicit requirements fail closed.

The imported proof's absolute local workstation `source` path is removed when
staging into a Player, while non-sensitive SHA-256 audit information remains.

### Step 3 — GPU and gameplay verification

The **Unity Windows Player real GPU visual acceptance** workflow and the
**Unity Editor on Windows self-hosted** workflow both run the local staging
step immediately after preparing the Map11 and Garen baseline and before Unity
build/test. No additional internet fetch is used for the optional models.

During `-hextechGameplayQA`, a four-actor source/rig smoke instantiates
Garen plus Blitzcrank/Riven/Yasuo slots and checks each staged original
`SkinnedMeshRenderer`, nonempty mesh, matching hero identity and actual
idle/run/attack1 clips. Missing optional art stays an accurately named tactical
marker. The smoke then restores its existing validated two-player hunter/hider
tests. The output `gameplay-result.json` contains
`optionalOriginalSources` and `optionalOriginalRigs`; a successful test with
both numbers zero **does not claim that extra original models were imported**.

This pipeline tests source integrity and runtime load. It does not yet verify
original character-specific scale, alignment, skinning deformation quality,
spell animation art direction, or rights to distribute the work. Those need
actual GPU screenshot review after the real sources are supplied.
