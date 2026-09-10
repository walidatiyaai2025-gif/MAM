"""Reference argv builders for reviewed local media only, not a full worker.
Pin and approve FFmpeg/FFprobe builds before integration.
No shell=True. Caller supplies canonical absolute paths from its native broker.
Not covered here: ACLs, scanning, process sandbox, queue, publication or backup.
"""
from pathlib import Path
import hashlib
import json
import subprocess

PROFILES = {
    'hires-v1': (1920, 1080, 18, '192k'),
    'lowres-v1': (854, 480, 26, '96k'),
}

def local_input(path):
    p = Path(path)
    if not p.is_absolute() or not p.is_file():
        raise ValueError('An existing absolute file path is required')
    return str(p.resolve(strict=True))

def sha256_file(path):
    h = hashlib.sha256()
    with open(local_input(path), 'rb') as f:
        while chunk := f.read(1024 * 1024):
            h.update(chunk)
    return h.hexdigest()

def probe_args(executable, source):
    return [str(executable), '-v', 'error', '-protocol_whitelist', 'file',
            '-show_format', '-show_streams', '-of', 'json', local_input(source)]

def probe(executable, source):
    result = subprocess.run(probe_args(executable, source), check=True,
                            capture_output=True, text=True, timeout=60)
    # Production: cap stdout/stderr while streaming; impose OS resource limits.
    return json.loads(result.stdout)

def transcode_args(executable, source, output, profile_id):
    width, height, crf, audio_rate = PROFILES[profile_id]
    out = Path(output)
    if not out.is_absolute() or out.suffix.lower() != '.mp4':
        raise ValueError('Output must be an absolute .mp4 staging path')
    if out.exists():
        raise FileExistsError(out)
    # Contract: SDR progressive square-pixel source, rotation normalized.
    # Non-square-pixel/HDR/interlaced inputs require a separate approved profile.
    vf = (f'scale={width}:{height}:force_original_aspect_ratio=decrease:'
          f'force_divisible_by=2,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,'
          'setsar=1')
    return [str(executable), '-hide_banner', '-nostdin', '-n',
            '-protocol_whitelist', 'file', '-i', local_input(source),
            '-map', '0:v:0', '-map', '0:a:0?', '-sn', '-dn',
            '-map_metadata', '-1', '-map_chapters', '-1',
            '-vf', vf, '-c:v', 'libx264', '-preset', 'medium',
            '-crf', str(crf), '-pix_fmt', 'yuv420p',
            '-c:a', 'aac', '-b:a', audio_rate, '-ar', '48000', '-ac', '2',
            '-movflags', '+faststart', '-progress', 'pipe:1', '-nostats',
            str(out)]

def assert_output(info, profile_id, had_audio):
    width, height, _, _ = PROFILES[profile_id]
    videos = [s for s in info['streams'] if s['codec_type'] == 'video']
    audios = [s for s in info['streams'] if s['codec_type'] == 'audio']
    assert len(videos) == 1
    v = videos[0]
    assert (v['width'], v['height']) == (width, height)
    assert v['codec_name'] == 'h264' and v['pix_fmt'] == 'yuv420p'
    assert v.get('sample_aspect_ratio') == '1:1'
    assert len(audios) == (1 if had_audio else 0)
    if had_audio:
        assert audios[0]['codec_name'] == 'aac'
        assert int(audios[0]['sample_rate']) == 48000
    assert float(info['format']['duration']) > 0
    # Production must additionally verify decode, duration, sync, rotation,
    # container, channels, and profile prerequisites; do not rely on asserts.

def sqlite_snapshot(connection, destination):
    import sqlite3
    dest = Path(destination)
    if not dest.is_absolute() or dest.exists():
        raise ValueError('Require new absolute backup destination')
    with sqlite3.connect(dest) as snapshot:
        connection.backup(snapshot)
        assert snapshot.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
    # This snapshots DB only. Freeze publication and capture referenced media
    # with a manifest for a consistent full-library backup.
