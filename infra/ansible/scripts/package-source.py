#!/usr/bin/env python3
"""Create a reproducible Docker context containing the API and Web sources only."""
import argparse
import gzip
import hashlib
import json
import os
from pathlib import Path
import tarfile

EXCLUDED = {".git", ".idea", ".vs", ".DS_Store", "node_modules", "bin", "obj", "dist", "__pycache__"}


def include(path):
    return path.name not in EXCLUDED and not path.name.startswith(".env") and not path.name.endswith((".user", ".tsbuildinfo", ".pyc"))


def package(source, destination):
    source = source.resolve()
    required = [source / name for name in ("src", "Web", ".dockerignore")]
    for path in required:
        if not path.exists():
            raise ValueError(f"Missing Docker context input: {path}")

    def metadata(info):
        info.uid = info.gid = info.mtime = 0
        info.uname = info.gname = ""
        return info

    def add(archive, path):
        if not include(path):
            return
        if path.is_symlink():
            raise ValueError(f"Source symlinks are not supported: {path}")
        archive.add(path, arcname=path.relative_to(source), recursive=False, filter=metadata)
        if path.is_dir():
            for child in sorted(path.iterdir()):
                add(archive, child)

    with destination.open("wb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0) as compressed:
            with tarfile.open(fileobj=compressed, mode="w") as archive:
                for path in required:
                    add(archive, path)
    os.chmod(destination, 0o600)
    return hashlib.sha256(destination.read_bytes()).hexdigest()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    print(json.dumps({"sha256": package(args.source_root, args.output)}))
