"""Package a built installer and its sources without copying private game data."""
from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import tempfile
import urllib.request
import zipfile

REPO = Path(__file__).resolve().parent.parent
RELEASE_FILES = (
    'installer/release.ps1', 'installer/release-notes.txt',
    'installer/codec-replacement.txt', 'installer/config/source-archives.json',
    'scripts/package-release.py',
)


def sha256(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def git(*arguments: str) -> str:
    return subprocess.check_output(['git', '-C', str(REPO), *arguments]).decode('utf-8')


def source_paths() -> list[str]:
    if (REPO / '.git').exists():
        paths = set(git('ls-files', '-z').split('\0')) - {''}
    else:
        # A release source ZIP has no Git database; reuse its explicit inventory.
        inventory = (REPO / 'SOURCE-SHA256SUMS.txt').read_text(encoding='utf-8')
        paths = {line.split('  ', 1)[1] for line in inventory.splitlines()}
        paths.discard('SOURCE-SNAPSHOT.txt')
    paths.update(RELEASE_FILES)
    selected = []
    for name in sorted(paths):
        parts = PurePosixPath(name).parts
        if PurePosixPath(name).is_absolute() or '..' in parts or '\\' in name:
            raise ValueError(f'Unsafe source path: {name}')
        if parts[0] in {'game', 'private', 'out', 'generated'}:
            if name != f'{parts[0]}/README.md':
                continue
        if (parts[0] in {'.git', '.agents', '.codex', '.aws'} or
                'bin' in parts or 'obj' in parts or '__pycache__' in parts or
                name.startswith('installer/out/') or
                Path(name).suffix.lower() in {'.log', '.pdb', '.sav', '.xnb', '.xwb', '.xsb', '.wav', '.live'}):
            continue
        path = REPO / name
        if not path.is_file() or path.is_symlink() or path.resolve() != path.absolute():
            raise ValueError(f'Unsafe or missing source file: {name}')
        selected.append(name)
    return selected


def write_zip(path: Path, entries: dict[str, bytes]) -> None:
    with zipfile.ZipFile(path, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(entries.items()):
            entry = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            archive.writestr(entry, data)
    with zipfile.ZipFile(path) as archive:
        bad = archive.testzip()
        if bad:
            raise ValueError(f'Corrupt ZIP entry: {bad}')


def get_archive(pin: dict, offline: bool) -> Path:
    name = pin['file']
    if Path(name).name != name:
        raise ValueError(f'Unsafe archive filename: {name}')
    path = REPO / 'out/deps/cache' / name
    if not path.exists():
        if offline:
            raise FileNotFoundError(f'Missing offline source archive: {name}')
        path.parent.mkdir(parents=True, exist_ok=True)
        partial = path.with_suffix(path.suffix + '.download')
        try:
            with urllib.request.urlopen(pin['url'], timeout=120) as response, partial.open('wb') as target:
                shutil.copyfileobj(response, target)
            if sha256(partial) != pin['sha256']:
                raise ValueError(f'Downloaded source hash differs: {name}')
            partial.replace(path)
        finally:
            partial.unlink(missing_ok=True)
    if sha256(path) != pin['sha256']:
        raise ValueError(f'Source archive hash differs: {name}')
    return path


def decoder_material(dependencies: dict, vgm_path: Path) -> dict[str, bytes]:
    decoder = dependencies['audioDecoder']
    payload = REPO / 'installer/out/payload/tools/audio'
    entries = {}
    root = f"vgmstream-{decoder['sourceCommit']}/"
    with zipfile.ZipFile(vgm_path) as archive:
        for pin in decoder['files']:
            path = payload / pin['name']
            if sha256(path) != pin['sha256']:
                raise ValueError(f'Installer decoder differs: {pin["name"]}')
            if path.suffix == '.dll':
                original = archive.read(root + 'ext_libs/dll-x64/' + path.name)
                if hashlib.sha256(original).hexdigest() != pin['sha256']:
                    raise ValueError(f'Decoder DLL does not match upstream source tree: {path.name}')
        for original, target in (
            ('doc/BUILD.md', 'build/BUILD.md'),
            ('doc/BUILD-LIB.md', 'build/BUILD-LIB.md'),
            ('ext_libs/extra/ffmpeg_options.txt', 'build/ffmpeg_options.txt'),
            ('ext_libs/extra/celt-0110/ecintrin.h', 'build/celt-0110/ecintrin.h'),
        ):
            entries[target] = archive.read(root + original)
    if os.name != 'nt':
        raise RuntimeError('Package on Windows to inspect the shipped FFmpeg build configuration.')
    lines = []
    with os.add_dll_directory(str(payload)):
        for library, prefix in (
            ('avutil-vgmstream-61.dll', 'avutil'), ('avcodec-vgmstream-63.dll', 'avcodec'),
            ('avformat-vgmstream-63.dll', 'avformat'), ('swresample-vgmstream-7.dll', 'swresample'),
        ):
            dll = ctypes.CDLL(str(payload / library))
            for suffix in ('configuration', 'license'):
                function = getattr(dll, prefix + '_' + suffix)
                function.restype = ctypes.c_char_p
                value = function().decode('utf-8')
                if suffix == 'license' and value != 'LGPL version 2.1 or later':
                    raise ValueError(f'Unexpected FFmpeg license: {value}')
                if suffix == 'configuration' and ('--enable-gpl' in value or '--enable-nonfree' in value):
                    raise ValueError('Unexpected FFmpeg build flags.')
                lines.append(f'{library} {suffix}:\n{value}\n')
    entries['ffmpeg-build.txt'] = '\n'.join(lines).encode('utf-8')
    return entries


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--offline', action='store_true', help='require cached source archives')
    args = parser.parse_args()
    setup = (REPO / 'installer/setup.iss').read_text(encoding='utf-8')
    version = re.search(r'^AppVersion=(\d+\.\d+\.\d+)$', setup, re.M).group(1)
    prefix = f'PIXEL-{version}'
    installer = REPO / f'installer/out/{prefix}.exe'
    if not installer.is_file():
        raise FileNotFoundError('Build the installer with installer/build.ps1 first.')
    dependencies = json.loads((REPO / 'config/dependencies.json').read_bytes())
    pins = json.loads((REPO / 'installer/config/source-archives.json').read_bytes())
    fna = dependencies['fna']
    all_pins = pins['archives'] + [dict(file=fna['cacheFile'], url=fna['archiveUrl'], sha256=fna['archiveSha256'])]
    archives = {pin['file']: get_archive(pin, args.offline) for pin in all_pins}
    third_party = decoder_material(dependencies, archives['vgmstream-d9875b1-source.zip'])
    for name, path in archives.items():
        third_party['sources/' + name] = path.read_bytes()
    third_party['source-archives.json'] = (json.dumps({'schemaVersion': 1, 'archives': all_pins}, indent=2) + '\n').encode()
    third_party['dependencies.json'] = (REPO / 'config/dependencies.json').read_bytes()
    third_party['README.txt'] = (REPO / 'installer/codec-replacement.txt').read_bytes()

    notices = sorted((REPO / 'installer/out/payload/runtime/notices').rglob('*'))
    notice_text = ['Arkedo Series - 03 PIXEL! third-party notices\n']
    for path in notices:
        if path.is_file():
            name = path.relative_to(REPO / 'installer/out/payload/runtime/notices').as_posix()
            data = path.read_bytes()
            third_party['notices/' + name] = data
            notice_text.append(f'\n===== {name} =====\n\n' + data.decode('utf-8-sig'))

    paths = source_paths()
    source = {f'{prefix}-source/{name}': (REPO / name).read_bytes() for name in paths}
    if (REPO / '.git').exists():
        provenance = ('Snapshot of working-tree files (including local edits).\n'
                      f'Base Git commit: {git("rev-parse", "HEAD").strip()}\n'
                      'The source archive contents, not the base commit alone, describe this build.\n')
    else:
        provenance = (REPO / 'SOURCE-SNAPSHOT.txt').read_text(encoding='utf-8')
    source[f'{prefix}-source/SOURCE-SNAPSHOT.txt'] = provenance.encode()
    source[f'{prefix}-source/SOURCE-SHA256SUMS.txt'] = ''.join(
        f'{hashlib.sha256(data).hexdigest()}  {name.removeprefix(prefix + "-source/")}\n'
        for name, data in sorted(source.items())).encode()

    release_root = REPO / 'out/release'
    release_root.mkdir(parents=True, exist_ok=True)
    # Build all artifacts in a new directory; never erase an earlier release.
    with tempfile.TemporaryDirectory(prefix='.package-', dir=release_root) as work:
        stage = Path(work)
        shutil.copyfile(installer, stage / installer.name)
        write_zip(stage / f'{prefix}-source.zip', source)
        write_zip(stage / f'{prefix}-third-party-sources.zip', third_party)
        shutil.copyfile(REPO / 'installer/release-notes.txt', stage / 'RELEASE-NOTES.txt')
        (stage / 'THIRD-PARTY-NOTICES.txt').write_text('\n'.join(notice_text), encoding='utf-8', newline='\n')
        (stage / 'SHA256SUMS.txt').write_text(''.join(
            f'{sha256(path)}  {path.name}\n' for path in sorted(stage.iterdir())), encoding='ascii', newline='\n')
        destination = release_root / prefix
        if destination.is_symlink() or destination.resolve() != destination.absolute():
            raise ValueError('Release destination must not contain reparse points.')
        destination.mkdir(exist_ok=True)
        for path in sorted(stage.iterdir()):
            target = destination / path.name
            if target.is_symlink() or target.resolve() != target.absolute():
                raise ValueError(f'Unsafe release target: {target}')
            shutil.copyfile(path, target)
    print(f'RELEASE_OK sourceFiles={len(paths)} sourceArchives={len(archives)} directory={destination}')
    for path in sorted(destination.iterdir()):
        print(f'{path.name}: {path.stat().st_size:,} bytes')


if __name__ == '__main__':
    main()
