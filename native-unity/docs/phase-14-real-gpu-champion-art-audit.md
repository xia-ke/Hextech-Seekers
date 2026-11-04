# Phase 14 — Windows GPU hero art acceptance and model diagnostics

## Purpose

Phase 13 made the four champion identities playable, but only **Garen**
ships a real original rig in the repository. Blitzcrank, Riven and Yasuo
were still clearly labeled tactical markers at the last verified run.
Phase 14 adds an **independent real GPU inspection of art source, skinned
geometry, animation clips and actual rendered frames**, instead of treating a
successful character-selection/gameplay smoke as proof of art completion.

The code **does not create, scrape, fabricate, publish or replace** original
champion meshes. Users must supply authorized source .lmesh, .lanim and .png
files as documented in [Phase 11](phase-11-original-champion-import.md).

## Windows acceptance mode

The release Player accepts a new independent argument:

```powershell
HextechSeekers.exe -force-d3d11 -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -hextechChampionVisualQA -hextechEvidenceDir "F:\HextechChampionQA"
```

A `NativeChampionVisualQA` driver loads actual original Map11 and cycles
Garen, Blitzcrank, Riven and Yasuo as the locally controlled hero. For each:

1. If the source is present, the hero **must** load its actual skinned mesh:
   matching hero identity, non-degenerate finite bounds, triangle indices,
   bone weights, bindposes, bones, supported shader, texture, original
   Idle/Run/Attack clips, and visible hero origin in the real camera.
2. The Player captures and verifies real PNGs:
   `champion-<hero>-idle.png`, `champion-<hero>-run.png`,
   `champion-<hero>-attack.png`.
3. If the original source was not supplied, the Player **must not** claim
   to have a real model. It verifies the explicit tactical marker and captures
   `champion-<hero>-missing-source-marker.png`. Missing optional assets do
   **not** fail the original Garen technical baseline.
4. If a source was supplied but its rig failed to load or degraded to a
   tactical marker, the test **fails closed**. The model cannot be silently
   accepted as the wrong hero or an unverified placeholder.

The `champion-visual-result.json` includes source vs. tactical-marker
classification, actual mesh vertex/triangle/bone counts, source mesh bounds
dimensions, original animation checks, camera pivot coverage, per-hero PNG
paths and whether Unity's Trial watermark is visible.

Successful technical capture status is
`CAPTURED_REQUIRES_VISUAL_REVIEW`, not "all original art approved." The
artistic review must still inspect visual size (relative to the original
Garen), heading/yaw, skin deformation, silhouette, materials, animation
timing, movement transitions and combat readability. There is no automatic
scale or rotation fix guessed from missing art references.

## CI and evidence

The Windows GPU workflow runs this phase after the validated playable match
and before the existing Phase 10 skill FX capture, using the **same actual
Windows Player and NVIDIA hardware GPU**. Evidence is archived into the
existing persistent Runner ZIP under
`F:\actions-runner\HextechVisualEvidence\hextech-gpu-<run-id>.zip`.

The workflow checks the complete four-hero source inventory and checks that
every PNG exists, has real PNG signature, at least 16 KiB of data and at
least 800×450 resolution. A staged original hero without legitimate mesh,
texture, idle/run/basic animation or frame evidence blocks CI. The
original-model count + missing-model count must equal exactly four.

NUnit tests in `NativeChampionRigAuditTests.cs` verify that mismatched
source/rig/marker states, invalid geometry dimensions and false original
identity are rejected. These synthetic tests contain no third-party art.

## Separate outstanding problems

- Original Blitzcrank, Riven, Yasuo model and animation sources are not on
  the self-hosted Runner yet; an optional import remains necessary.
- Game skills use functional diagnostic VFX; this QA does not sign off original
  spell effect art or copyrighted media distribution rights.
- The Unity Trial watermark is recorded but remains unmodified and
  non-blocking, per the project owner's earlier instruction.
