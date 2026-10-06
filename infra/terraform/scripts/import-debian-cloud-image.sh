#!/usr/bin/env bash
set -euo pipefail

: "${TRUENAS_SSH_TARGET:?Set truenas_ssh_target as user@host}"
: "${TRUENAS_ZVOL_PATH:?Target zvol path is required}"
: "${DEBIAN_IMAGE_VERSION:?Pinned Debian image version is required}"
: "${DEBIAN_IMAGE_SHA512:?Pinned Debian image SHA-512 is required}"
ssh_port="${TRUENAS_SSH_PORT:-22}"
identity_file="${TRUENAS_SSH_IDENTITY_FILE:-}"

if [[ ! "$DEBIAN_IMAGE_VERSION" =~ ^[0-9]{8}-[0-9]+$ ]]; then
  echo "Invalid Debian build version: $DEBIAN_IMAGE_VERSION" >&2
  exit 2
fi
if [[ ! "$DEBIAN_IMAGE_SHA512" =~ ^[A-Za-z0-9+/]{86}$ ]]; then
  echo "Invalid SHA-512 digest format" >&2
  exit 2
fi
if [[ ! "$TRUENAS_SSH_TARGET" =~ ^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+$ ]]; then
  echo "Invalid SSH target; use user@host" >&2
  exit 2
fi
if [[ ! "$ssh_port" =~ ^[0-9]+$ ]]; then
  echo "Invalid SSH port: $ssh_port" >&2
  exit 2
fi
if [[ ! "$TRUENAS_ZVOL_PATH" =~ ^/dev/zvol/[A-Za-z0-9_.:/-]+$ ]]; then
  echo "Refusing unexpected zvol path: $TRUENAS_ZVOL_PATH" >&2
  exit 2
fi

artifact="debian-13-generic-amd64-daily-${DEBIAN_IMAGE_VERSION}.qcow2"
ssh_args=(-p "$ssh_port" -o BatchMode=yes)
scp_args=(-P "$ssh_port" -o BatchMode=yes)
if [[ -n "$identity_file" ]]; then
  ssh_args+=(-i "$identity_file")
  scp_args+=(-i "$identity_file")
fi

work_dir="$(mktemp -d "${TMPDIR:-/tmp}/bikehub-image-import.XXXXXX")"
remote_script="/tmp/bikehub-image-import-${DEBIAN_IMAGE_VERSION}-$$.sh"
cleanup() {
  rm -f "$work_dir/import.sh"
  rmdir "$work_dir"
}
trap cleanup EXIT

cat > "$work_dir/import.sh" <<'REMOTE_SCRIPT'
set -euo pipefail
device="$1"
version="$2"
expected_sha512="$3"
artifact="debian-13-generic-amd64-daily-${version}.qcow2"
url="https://cloud.debian.org/images/cloud/trixie/daily/${version}/${artifact}"
image="$(mktemp "/tmp/${artifact}.XXXXXX")"
trap 'rm -f "$image"' EXIT

test -b "$device"
command -v qemu-img >/dev/null
command -v curl >/dev/null
command -v openssl >/dev/null

first_block="$(dd if="$device" bs=4096 count=1 status=none | od -An -tu1)"
if [[ -n "${first_block//[[:space:]0]/}" ]]; then
  echo "Refusing to overwrite non-empty zvol: $device" >&2
  exit 1
fi

curl --fail --location --retry 3 --output "$image" "$url"
actual_sha512="$(openssl dgst -sha512 -binary "$image" | openssl base64 -A | tr -d '=')"
if [[ "$actual_sha512" != "$expected_sha512" ]]; then
  echo "Debian cloud image checksum mismatch" >&2
  exit 1
fi

qemu-img convert -p -f qcow2 -O raw "$image" "$device"
echo "Imported verified Debian image $version into $device"
REMOTE_SCRIPT

scp "${scp_args[@]}" "$work_dir/import.sh" "$TRUENAS_SSH_TARGET:$remote_script"
echo "Downloading, verifying, and importing Debian $DEBIAN_IMAGE_VERSION on TrueNAS..."
ssh -tt "${ssh_args[@]}" "$TRUENAS_SSH_TARGET" \
  "sudo bash '$remote_script' '$TRUENAS_ZVOL_PATH' '$DEBIAN_IMAGE_VERSION' '$DEBIAN_IMAGE_SHA512'; status=\$?; rm -f '$remote_script'; exit \$status"

echo "Debian cloud image import completed."
