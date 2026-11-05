# Phase 17B — source-backed native Map11 jungle targets and Yasuo E Player QA

## Coordinates and provenance

This is a **source-derived** camp catalog, not a collection of estimated coordinates.

- Upstream: `iamywl/Web_game`, pinned commit `557a9d5352af12bee8bf3db879c22d04d0a0954e`.
- Placement source: `public/assets/maps/summoners-rift/placements.json`, `source.entry=data/maps/mapgeometry/map11/base_srx.materials.bin`, SHA-256 `b8709c64290fe8327ee1d14e927628efddd6dcea9ef1968989098d8477e71c3c`.
- Upstream source `sourcePosition=[sourceX, sourceHeight, sourceZ]` is converted into the native 3D navigation coordinates `x=sourceX`, `z=-sourceZ`. **Do not** use the scaled 3200×3200 web `x,y` fields as native coordinates.
- The exact navigation binary is the preserved `dist/assets/visual/summoners-rift/navigation.bin` (295×296, 6 bytes/cell). The original pinned default map GLB is SHA-256 `bdabbf3eb0c37dd2bb8567035c018d0da0f79c849c9353e5192a58279369a69e`.
- Independently checked each admitted catalog position against `RiftGrid`'s real 3×3 walkable clearance, lower-half `x+z >= -120` region, and at least one legal Yasuo E approach/dash landing on actual navigation. The `manuallyVerified` compatibility flag denotes this source-and-navigation review, **not** claimed visual verification of original jungle monster meshes or animations.

Only 8 source-backed camps qualify in the current lower-half native gameplay region:

| Camp | Native x | Native z | Source ID |
| --- | ---: | ---: | --- |
| Chaos Blue | 11031.729 | -6990.844 | `0xb0528add` |
| Order Red | 7765.244 | -4020.187 | `0x5f8d7bea` |
| Chaos OwlBear | 12625.562 | -6434.189 | `0x789acc87` |
| Chaos Wolves | 10979.800 | -8356.890 | `0xf15c8aff` |
| Order Wraiths | 6969.000 | -5422.000 | `0x9d0e7aac` |
| Order Small Golems | 8390.000 | -2675.000 | `0x44b899d1` |
| Dragon Crab | 10500.000 | -5170.000 | `0x6ae501` |
| Dragon | 9837.674 | -4397.761 | `0x4a7f091d` |

The other 10 source locators from upstream `camps` include opposite-half positions or alternative objective variants outside the existing allowed navigation. They are deliberately excluded, rather than relocated to invented positions.

## Scope and strict acceptance

- `Assets/StreamingAssets/Rift/camps.json` is loaded by the existing `NativeCampCatalog.LoadVerified` path on Unity Player initialization. Eight passive Unity scene diagnostic entities are created. These are **not** the original jungle model GLBs.
- Player QA creates a real Yasuo hider match, finds an honest navigable dash origin near an imported source camp, removes any visible opposing hero from range, invokes the **same** `TryHumanSkill(E)` method used by keyboard gameplay, then checks target-dependent dash, precise walkable landing, no capture, target remains alive/nonaggressive and repeat-lock after 0.6s (global cooldown 0.5s).
- QA must fail if the catalog was not packaged or contains an unexpected number of camps, if no real approach/exit is found, or if the cast/lock assertions fail. Never substitute a mock target or synthesize a success record.
- JSON evidence `neutralSkillSmokesExpected/Passed/Evidence` is independently verified by the Windows GPU workflow; all pre-existing 10 basic, 4 target/chain, and 1 vision skill QA gates remain required.

## Limitations

The upstream `public/assets/units/jungle/manifest.json` documents provenance of original monster GLBs and idle/attack clips; those assets have **not** been staged or rendered in the native Unity build. Existing translucent/cyan diagnostic rings must not be described as the original animal meshes. The original upstream web game has offensive monster AI; native hide-and-seek **deliberately** keeps monsters indestructible, nonaggressive and non-capturing.
