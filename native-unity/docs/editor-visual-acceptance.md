# Unity Editor / Windows GPU real rendering acceptance

**Current status: PENDING.** GitHub Python static verification is **not** proof of Unity Editor compilation or rendered pixels.

## Editor compilation and NUnit

- Unity version: 6000.0.40f1; project: `native-unity/`.
- Pinned assets prepared by `python native-unity/tools/prepare_assets.py`.
- Manually dispatch [Unity Editor compile and tests](../../.github/workflows/unity-editor-validation.yml) after configuring repository Actions secrets: `UNITY_LICENSE` or licensed `UNITY_SERIAL` plus account credentials as appropriate. GameCI runner imports the actual Unity project and runs Editor tests; captures Unity logs and NUnit report as GitHub artifacts.
- A missing license intentionally fails with a clear error; it does not silently claim compilation success.

## Real 3D GPU screenshot acceptance

1. On Windows machine with Unity Editor + Windows Build Support and a real GPU, open the `native-unity/` project in Unity Hub.
2. Choose menu `Hextech Seekers > 1. Create Native Rift Phase 2`; verify the generated scene compiles without errors. Load the original GLB; verify Garen's body, sword and skin have correct orientation/UVs.
3. Build the Windows x64 player via `Hextech Seekers > 2. Build Windows` and run `HextechSeekers.exe -hextechVisualQA` with normal graphics. The `NativeVisualAcceptance` component writes `result.json` and three screenshots to `Application.persistentDataPath/HextechVisualQA`.
4. Inspect `native-idle.png`, `native-run.png`, `native-attack.png` (original 3D Rift background and real Garen mesh required); compare root position to ground, winding, transparency, map orientation, animation quality and visibility against the browser reference.
5. Verify screenshots are legible and vary across clips; review GPU FPS, frame pacing, device/API, Unity `Player.log`. A JSON status `CAPTURED_REQUIRES_VISUAL_REVIEW` means screenshot files were produced, **not** that art orientation, true FPS or game release is approved.
6. Attach logs, screenshots and NUnit results to an issue or GitHub artifact. Do not mark phase complete until a real Unity Editor run and GPU screenshot review have occurred.

## Automated checks still outstanding

- Unity graphics validation cannot be inferred from headless `-nographics` compilation or from screenshots of the former Canvas web game.
- The standalone Windows build, coordinates, bind matrices, animation interpolation, URP material and texture UVs require a real editor/toolchain.
- The four-character playable game, full visibility and game rules remain outside this acceptance gate.
