#!/usr/bin/env python3
"""Validate actual Unity Windows GPU screenshots without third-party Python packages.

This checks structure, color diversity, source scene/hero flags and animations.
It CANNOT judge visual fidelity, camera framing, rig deformation or Riot artwork
alignment; those require looking at the uploaded screenshots.
"""
import argparse
import hashlib
import json
import struct
import sys
import zlib
from pathlib import Path

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
REQUIRED = ("native-idle.png", "native-run.png", "native-attack.png")


def inspect_png(path):
    data = path.read_bytes()
    if not data.startswith(PNG_SIGNATURE):
        raise ValueError(f"{path.name}: not a PNG")
    cursor, compressed, dims = len(PNG_SIGNATURE), [], None
    while cursor + 12 <= len(data):
        length = struct.unpack_from(">I", data, cursor)[0]
        kind = data[cursor + 4:cursor + 8]
        start, end = cursor + 8, cursor + 8 + length
        if end + 4 > len(data):
            raise ValueError(f"{path.name}: truncated chunk")
        payload = data[start:end]
        expected_crc = struct.unpack_from(">I", data, end)[0]
        if zlib.crc32(kind + payload) & 0xFFFFFFFF != expected_crc:
            raise ValueError(f"{path.name}: PNG CRC mismatch")
        if kind == b"IHDR":
            dims = struct.unpack(">IIBBBBB", payload)
        elif kind == b"IDAT":
            compressed.append(payload)
        elif kind == b"IEND":
            break
        cursor = end + 4
    if dims is None or not compressed:
        raise ValueError(f"{path.name}: missing IHDR/IDAT")
    width, height, depth, color_type, compression, filter_method, interlaced = dims
    if width < 640 or height < 360 or depth != 8 or color_type not in (2, 6) or interlaced:
        raise ValueError(f"{path.name}: unexpected pixel format/resolution: {dims}")
    if compression != 0 or filter_method != 0:
        raise ValueError(f"{path.name}: unsupported PNG encoding")
    bpp = 3 if color_type == 2 else 4
    stride = width * bpp
    raw = zlib.decompress(b"".join(compressed))
    if len(raw) != (stride + 1) * height:
        raise ValueError(f"{path.name}: decoded PNG byte count mismatch")
    previous = bytearray(stride)
    palette = set()
    brightness = []
    step_x = max(width // 32, 1)
    step_y = max(height // 24, 1)
    offset = 0
    for y in range(height):
        filter_type = raw[offset]
        row = bytearray(raw[offset + 1:offset + stride + 1])
        offset += stride + 1
        if filter_type not in (0, 1, 2, 3, 4):
            raise ValueError(f"{path.name}: unsupported PNG scanline filter {filter_type}")
        if filter_type:
            for i in range(stride):
                a = row[i - bpp] if i >= bpp else 0
                b = previous[i]
                c = previous[i - bpp] if i >= bpp else 0
                if filter_type == 1:
                    predictor = a
                elif filter_type == 2:
                    predictor = b
                elif filter_type == 3:
                    predictor = (a + b) // 2
                else:
                    p = a + b - c
                    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                    predictor = a if pa <= pb and pa <= pc else (b if pb <= pc else c)
                row[i] = (row[i] + predictor) & 255
        if y % step_y == 0:
            for x in range(0, width, step_x):
                rgb = tuple(row[x * bpp:x * bpp + 3])
                palette.add(rgb)
                brightness.append((rgb[0] * 299 + rgb[1] * 587 + rgb[2] * 114) // 1000)
        previous = row
    diversity = len(palette)
    dynamic_range = max(brightness) - min(brightness)
    if diversity < 24 or dynamic_range < 25:
        raise ValueError(
            f"{path.name}: likely blank/flat capture; colors={diversity}, luminance-range={dynamic_range}"
        )
    return {
        "width": width, "height": height,
        "bytes": len(data), "sampledDistinctColors": diversity,
        "sampledLuminanceRange": dynamic_range,
        "sha256": hashlib.sha256(data).hexdigest(),
    }


def verify(directory):
    report_path = directory / "result.json"
    if not report_path.is_file():
        raise ValueError("Player did not produce result.json; inspect Player.log")
    player = json.loads(report_path.read_text(encoding="utf-8-sig"))
    if player.get("status") != "CAPTURED_REQUIRES_VISUAL_REVIEW":
        raise ValueError("Player capture status: " + str(player.get("status")) +
                         " -- " + str(player.get("reason")))
    expected_true = (
        "originalSceneLoaded", "skinnedModelExists", "cameraSeesHero",
        "hardwareGraphics", "hasIdle", "hasRun", "hasAttack",
    )
    for key in expected_true:
        if player.get(key) is not True:
            raise ValueError(f"Player report {key} is not true")

    # Trial-license watermarks are NOT removed from images and are tracked
    # explicitly as warnings. They do not block technical GPU acceptance.
    # Development/debug overlays, by contrast, must stay disabled in QA.
    if not isinstance(player.get("unityWatermarkVisible"), bool):
        raise ValueError("Player did not report a boolean unityWatermarkVisible")
    if player.get("trialWatermarkIgnoredForTechnicalQa") is not player["unityWatermarkVisible"]:
        raise ValueError("Trial watermark warning status does not match Unity's visibility report")
    for key in ("developmentWatermarkEnabled", "debugHudEnabled"):
        if player.get(key) is not False:
            raise ValueError(f"Player report {key} must be false; got {player.get(key)!r}")

    conversion_path = directory / "map-conversion.json"
    if not conversion_path.is_file():
        raise ValueError("Missing map-conversion.json; alpha-channel/material audit was not attached")
    conversion = json.loads(conversion_path.read_text(encoding="utf-8-sig"))
    alpha_audit = conversion.get("alphaAudit")
    if not isinstance(alpha_audit, dict):
        raise ValueError("map-conversion.json has no alphaAudit report")
    suspicious_foliage = [
        item for item in alpha_audit.get("materials", [])
        if item.get("alphaMode") == "OPAQUE"
        and any(token in str(item.get("name", "")).lower()
                for token in ("leaf", "leaves", "foliage", "grass", "tree", "bush", "plant", "shrub"))
    ]
    if suspicious_foliage:
        names = ", ".join(str(item.get("name") or item.get("materialIndex"))
                          for item in suspicious_foliage[:12])
        raise ValueError(
            "Vegetation base-color textures contain transparency but their glTF materials use OPAQUE alphaMode: " +
            names
        )

    if player.get("mapRendererCount", 0) < 1:
        raise ValueError("No 3D map renderers were instantiated")
    if player.get("frames", 0) < 100:
        raise ValueError("Player did not render 100 frames")
    if player.get("fpsAverage", 0) <= 0:
        raise ValueError("Invalid measured FPS")
    if player.get("screenshotCount") != 3 or set(player.get("screenshots", [])) != set(REQUIRED):
        raise ValueError("Screenshot state list incomplete")
    images = {}
    for name in REQUIRED:
        path = directory / name
        if not path.is_file() or path.stat().st_size < 16384:
            raise ValueError(f"Screenshot absent/truncated: {name}")
        images[name] = inspect_png(path)
    return {
        "status": "AUTOMATED_CAPTURE_PASS_VISUAL_REVIEW_REQUIRED",
        "visualFidelityAccepted": False,
        "editorVersion": player.get("unityVersion"),
        "graphicsApi": player.get("graphicsApi"),
        "gpu": player.get("device"),
        "scene": player.get("scene"),
        "mapRendererCount": player.get("mapRendererCount"),
        "fpsAverage": player.get("fpsAverage"),
        "worstFrameMilliseconds": player.get("worstFrameMilliseconds"),
        "unityWatermarkVisible": player.get("unityWatermarkVisible"),
        "trialWatermarkIgnoredForTechnicalQa": player.get("trialWatermarkIgnoredForTechnicalQa"),
        "technicalWarning": (
            "Unity Trial watermark remains visible in evidence; ignored for technical smoke QA"
            if player["unityWatermarkVisible"] else None
        ),
        "developmentWatermarkEnabled": player.get("developmentWatermarkEnabled"),
        "debugHudEnabled": player.get("debugHudEnabled"),
        "alphaAudit": alpha_audit,
        "screenshots": images,
        "remainingVisualChecks": [
            "Confirm original 3D map geometry, materials and lighting are legible",
            "Confirm Garen faces the correct direction, with correct UVs and skeleton deformation",
            "Confirm map/hero scale, vertical alignment and 55-degree camera framing",
            "Confirm screenshots show visibly different idle/run/attack poses",
        ],
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    directory = args.directory.resolve()
    directory.mkdir(parents=True, exist_ok=True)
    try:
        result = verify(directory)
        (directory / "verification.json").write_text(
            json.dumps(result, indent=2, ensure_ascii=False), encoding="utf-8"
        )
        lines = [
            "HEXTECH SEEKERS - WINDOWS GPU PLAYER TECHNICAL ACCEPTANCE",
            "AUTOMATED CAPTURE PASS; VISUAL ART REVIEW STILL REQUIRED",
            "GPU: " + str(result["gpu"]),
            "Graphics API: " + str(result["graphicsApi"]),
            "Unity: " + str(result["editorVersion"]),
            "Map renderers: " + str(result["mapRendererCount"]),
            "Average FPS: " + str(result["fpsAverage"]),
            "Trial watermark visible (non-blocking): " + str(result["unityWatermarkVisible"]),
            "",
            "Inspect native-idle.png, native-run.png, native-attack.png side-by-side.",
            "Do not claim scene/animation fidelity accepted until manual visual inspection.",
        ]
        (directory / "README.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
        print(json.dumps(result, indent=2, ensure_ascii=False))
        return 0
    except Exception as exc:
        failure = {"status": "FAIL_GPU_VISUAL_ACCEPTANCE", "error": str(exc)}
        (directory / "verification.json").write_text(
            json.dumps(failure, indent=2), encoding="utf-8"
        )
        print("GPU VISUAL QA FAILED: " + str(exc), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
