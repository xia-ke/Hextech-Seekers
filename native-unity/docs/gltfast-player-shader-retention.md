# Windows Player: glTFast Shader Graph retention

## Observed failure

The November 2, 2026 Windows Player screenshot showed a full magenta Rift surface and two runtime errors:

- `Shader Graphs/glTF-unlit` is missing.
- `Shader Graphs/glTF-pbrMetallicRoughness` is missing.

These are the URP Shader Graph assets used by glTFast 6.20.0 for runtime glTF materials. This is not a missing GLB geometry or GPU-driver failure. glTFast's Project Setup documentation explains that a missing shader asset makes runtime material generation return null, leaving the affected mesh primitive magenta.

## Fix

`HextechNativeSetup.BuildWindows()` now creates two serialized placeholder materials under:

- `Assets/Resources/Native/GLTFast/GltfUnlit.mat`
- `Assets/Resources/Native/GLTFast/GltfPbrMetallicRoughness.mat`

Each material directly references the corresponding package shader. This is the documented glTFast/Unity method for retaining shaders that are otherwise only discovered dynamically at runtime.

`RiftBootstrap` loads and holds these Resources materials before starting glTFast import. After scene instantiation it audits every renderable mesh for a non-null, supported material and rejects the error shader. A failed audit prevents `IsReady` from becoming true.

The Windows GPU workflow now requires these log markers:

- `NATIVE_GLTF_SHADER_RESOURCES_READY` during build.
- `NATIVE_GLTF_SHADERS_PRELOADED` before runtime import.
- `NATIVE_MAP_MATERIAL_AUDIT_PASS` after map instantiation.

It also fails if Player.log contains either of the two known missing-shader messages.

## Build presentation

The previous build explicitly used `BuildOptions.Development | BuildOptions.AllowDebugging`, which caused the user-facing Development Build watermark and Development Console. The playable Windows build now uses `BuildOptions.None`. CI still captures logs with the explicit `-logFile` command-line argument and runs the same gameplay/visual QA flags.

## Regression tests

- `GltfFastShaderBuildTests` asserts both package Shader Graph names resolve and are supported in the Unity Editor.
- The real Windows GPU Player acceptance validates the retained Resources materials, imported map materials, screenshots, gameplay smoke, and Phase 10 FX.
- A successful Unity build alone is not visual acceptance; the GPU Player checks must also pass.

## Scope

This fix addresses the missing glTFast URP shader assets observed in the screenshot. It does not claim final art-direction acceptance; the resulting Rift screenshots still require visual review for terrain textures, lighting, camera framing, and hero appearance.
