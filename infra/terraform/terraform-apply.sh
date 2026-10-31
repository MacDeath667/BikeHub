#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

if [[ $# -gt 0 && "$1" == "-help" ]]; then
  exec terraform -chdir="$script_dir" apply "$@"
fi

if [[ -z "${TRUENAS_SUDO_PASSWORD:-}" ]]; then
  read -r -s -p "TrueNAS sudo password (image/seed provisioning only): " TRUENAS_SUDO_PASSWORD
  printf '\n'
fi
if [[ -z "$TRUENAS_SUDO_PASSWORD" ]]; then
  echo "A non-empty TrueNAS sudo password is required." >&2
  exit 2
fi
export TRUENAS_SUDO_PASSWORD

exec terraform -chdir="$script_dir" apply "$@"
