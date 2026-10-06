#!/usr/bin/env bash
set -euo pipefail

: "${TRUENAS_SSH_TARGET:?Set truenas_ssh_target as user@host}"
: "${CLOUD_INIT_ISO_PATH:?Cloud-init ISO path on TrueNAS is required}"
: "${CLOUD_INIT_INSTANCE_ID:?Cloud-init instance ID is required}"
: "${CLOUD_INIT_USER_DATA:?Rendered cloud-init user-data is required}"

ssh_port="${TRUENAS_SSH_PORT:-22}"
identity_file="${TRUENAS_SSH_IDENTITY_FILE:-}"

if [[ ! "$TRUENAS_SSH_TARGET" =~ ^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+$ ]]; then
  echo "Invalid SSH target; use user@host" >&2
  exit 2
fi
if [[ ! "$ssh_port" =~ ^[0-9]+$ ]]; then
  echo "Invalid SSH port: $ssh_port" >&2
  exit 2
fi
if [[ ! "$CLOUD_INIT_INSTANCE_ID" =~ ^[A-Za-z0-9._-]+$ ]]; then
  echo "Invalid cloud-init instance ID" >&2
  exit 2
fi
if [[ ! "$CLOUD_INIT_ISO_PATH" =~ ^/mnt/[A-Za-z0-9_./-]+$ || "$CLOUD_INIT_ISO_PATH" == *".."* ]]; then
  echo "Cloud-init ISO must be a safe absolute path under /mnt" >&2
  exit 2
fi

work_dir="$(mktemp -d "${TMPDIR:-/tmp}/bikehub-cloud-init.XXXXXX")"
mkdir "$work_dir/payload"
cleanup() {
  rm -f "$work_dir/payload/user-data" "$work_dir/payload/meta-data" "$work_dir/seed.iso"
  rmdir "$work_dir/payload"
  rmdir "$work_dir"
}
trap cleanup EXIT

printf '%s\n' "$CLOUD_INIT_USER_DATA" > "$work_dir/payload/user-data"
cat > "$work_dir/payload/meta-data" <<EOF
instance-id: ${CLOUD_INIT_INSTANCE_ID}
local-hostname: ${CLOUD_INIT_INSTANCE_ID}
EOF

if command -v hdiutil >/dev/null 2>&1; then
  hdiutil makehybrid -iso -joliet -default-volume-name cidata \
    -o "$work_dir/seed.iso" "$work_dir/payload" >/dev/null
elif command -v genisoimage >/dev/null 2>&1; then
  genisoimage -quiet -output "$work_dir/seed.iso" -volid cidata \
    -rational-rock -joliet "$work_dir/payload/user-data" "$work_dir/payload/meta-data"
else
  echo "Install hdiutil (macOS) or genisoimage to create the NoCloud seed ISO" >&2
  exit 1
fi

ssh_args=(-p "$ssh_port" -o BatchMode=yes)
scp_args=(-P "$ssh_port" -o BatchMode=yes)
if [[ -n "$identity_file" ]]; then
  ssh_args+=(-i "$identity_file")
  scp_args+=(-i "$identity_file")
fi

remote_tmp="/tmp/bikehub-cloud-init-${CLOUD_INIT_INSTANCE_ID}.iso"
scp "${scp_args[@]}" "$work_dir/seed.iso" "$TRUENAS_SSH_TARGET:$remote_tmp"
ssh -T "${ssh_args[@]}" "$TRUENAS_SSH_TARGET" \
  "sudo -n install -D -o root -g root -m 0644 '$remote_tmp' '$CLOUD_INIT_ISO_PATH' && sudo -n rm -f '$remote_tmp'"

echo "Installed NoCloud seed ISO at $CLOUD_INIT_ISO_PATH"
