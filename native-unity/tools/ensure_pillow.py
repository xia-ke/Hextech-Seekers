#!/usr/bin/env python3
"""Provision Pillow WebP decoder on a Windows self-hosted runner.

Python 3.8 supports Pillow 10.4.0, not Pillow >=11.
Fetch a matching wheel from official PyPI metadata, verify SHA-256 and install
from the downloaded local wheel (pip has no network access). Never install a
wheel from an unverified URL or use --trusted-host.
"""
import hashlib
import json
import os
import platform
import subprocess
import sys
import urllib.request
from pathlib import Path

PIN = '10.4.0'
METADATA_URL = 'https://pypi.org/pypi/pillow/{}/json'.format(PIN)


def has_webp():
    try:
        from PIL import Image, features
        if features.check('webp'):
            print('PIL_WEBP_READY: Pillow {} and WebP decoder available'.format(Image.__version__), flush=True)
            return True
    except (ImportError, AttributeError, OSError) as exc:
        print('PIL missing/unusable: {}'.format(exc), flush=True)
    return False


def get_url(url, *, expected_sha=None, max_bytes=8_000_000):
    last = None
    # Standard urllib proxy settings work for downloading pinned Rift map,
    # whereas old pip's urllib3 proxy TLS layer fails on this runner.
    for use_environment_proxy in (True, False):
        opener = urllib.request.build_opener(
            urllib.request.ProxyHandler() if use_environment_proxy
            else urllib.request.ProxyHandler({})
        )
        try:
            req = urllib.request.Request(url, headers={'User-Agent': 'HextechSeekers-PillowWheel/1.0'})
            with opener.open(req, timeout=35) as response:
                chunks = []
                total = 0
                while True:
                    data = response.read(1024 * 1024)
                    if not data:
                        break
                    total += len(data)
                    if total > max_bytes:
                        raise ValueError('Download exceeds authorized byte limit')
                    chunks.append(data)
            payload = b''.join(chunks)
            if expected_sha and hashlib.sha256(payload).hexdigest().lower() != expected_sha.lower():
                raise ValueError('Wheel SHA256 verification failed')
            return payload
        except Exception as exc:
            last = exc
            print('Official PyPI download attempt (proxy={}): {}'.format(
                use_environment_proxy, exc), flush=True)
    raise RuntimeError('Failed downloading {}: {}'.format(url, last))


def main():
    if has_webp():
        return 0
    if platform.system() != 'Windows':
        raise RuntimeError('This wheel installer is only for Windows self-hosted runner')
    if sys.implementation.name != 'cpython' or sys.maxsize <= 2**32:
        raise RuntimeError('Requires 64-bit CPython on Windows')
    if sys.version_info[:2] < (3, 8) or sys.version_info[:2] > (3, 12):
        raise RuntimeError('Expected Windows CPython 3.8-3.12 for pinned Pillow 10.4 wheel')
    print('Python executable:', sys.executable, 'version:', sys.version, flush=True)
    data = get_url(METADATA_URL, max_bytes=2_000_000)
    meta = json.loads(data)
    if meta.get('info', {}).get('version') != PIN:
        raise RuntimeError('Incorrect Pillow release metadata')
    ver = 'cp{}{}'.format(sys.version_info[0], sys.version_info[1])
    suffix = '{}-{}-win_amd64.whl'.format(ver, ver)
    wheel_info = next((u for u in meta.get('urls', [])
                       if u.get('filename', '').endswith(suffix)), None)
    if not wheel_info:
        raise RuntimeError('Official Pillow {} wheel for {} not found'.format(PIN, suffix))
    url = wheel_info['url']
    if not url.startswith('https://files.pythonhosted.org/'):
        raise RuntimeError('Unexpected wheel hosting domain, refusing download')
    expected = wheel_info['digests']['sha256']
    wheel = get_url(url, expected_sha=expected, max_bytes=9_000_000)
    cache = Path(os.environ.get('RUNNER_TEMP') or Path.cwd()) / 'hextech-pillow-wheels'
    cache.mkdir(parents=True, exist_ok=True)
    dest = cache / wheel_info['filename']
    dest.write_bytes(wheel)
    print('OFFICIAL_WHEEL_SHA256_OK:', dest.name, expected, flush=True)
    args = [sys.executable, '-m', 'pip', 'install',
            '--disable-pip-version-check', '--no-index', '--no-deps']
    if sys.prefix == sys.base_prefix:
        args.append('--user')
    args.append(str(dest))
    proc = subprocess.run(args, timeout=160, check=False)
    if proc.returncode:
        raise RuntimeError('Local offline Pillow wheel installation exited {}'.format(proc.returncode))
    if not has_webp():
        raise RuntimeError('Offline Pillow installation finished but WebP decoder remains unavailable')
    print('PILLOW_PROVISIONED_OFFLINE', flush=True)
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception as exc:
        print('PILLOW_SETUP_ERROR: {}'.format(exc), file=sys.stderr, flush=True)
        sys.exit(1)
