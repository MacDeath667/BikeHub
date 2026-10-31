#!/usr/bin/env bash
set -euo pipefail

if [[ $# -gt 0 && "$1" == "-help" ]]; then
  exec terraform apply "$@"
fi

read -r -s -p "TrueNAS sudo password: " TRUENAS_SUDO_PASSWORD
printf '\n'
if [[ -z "$TRUENAS_SUDO_PASSWORD" ]]; then
  echo "A non-empty TrueNAS sudo password is required." >&2
  exit 2
fi
export TRUENAS_SUDO_PASSWORD

exec terraform apply "$@"
