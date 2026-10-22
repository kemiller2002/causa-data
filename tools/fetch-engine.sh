#!/usr/bin/env bash
# Fetches the pinned rehearsal domain engine into .engine/ and verifies its commit.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
repo="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["repository"])' "$root/engine.lock.json")"
commit="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["commit"])' "$root/engine.lock.json")"
dest="$root/.engine"
if [ ! -d "$dest/.git" ]; then
  git init -q "$dest"
  git -C "$dest" remote add origin "$repo"
fi
if [ "$(git -C "$dest" rev-parse -q --verify HEAD 2>/dev/null || true)" != "$commit" ]; then
  git -C "$dest" fetch -q --depth 1 origin "$commit"
  git -C "$dest" checkout -q --detach FETCH_HEAD
fi
actual="$(git -C "$dest" rev-parse HEAD)"
if [ "$actual" != "$commit" ]; then
  echo "engine commit mismatch: expected $commit, got $actual" >&2
  exit 1
fi
if [ -n "$(git -C "$dest" status --porcelain --untracked-files=no)" ]; then
  echo "engine checkout has local modifications; refusing to use it" >&2
  exit 1
fi
echo "engine $actual"
