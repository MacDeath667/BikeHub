#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
logs_only=false
rebuild=true

usage() {
  cat <<'USAGE'
Usage: scripts/redeploy.sh [--logs-only] [--no-build]

Updates Grafana provisioning and deploys BikeHub, then follows API logs.
  --logs-only  Follow API logs without deploying.
  --no-build   Apply configuration using the current image without rebuilding.
  -h, --help   Show this help.

Environment:
  BIKEHUB_VM_IP     VM address (default: 192.168.0.49).
  BIKEHUB_SSH_KEY   VM SSH key (default: ~/.ssh/truenas_admin).
USAGE
}

for argument in "$@"; do
  case "$argument" in
    --logs-only) logs_only=true ;;
    --no-build) rebuild=false ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'Unknown argument: %s\n' "$argument" >&2; usage >&2; exit 2 ;;
  esac
done

vm_address="${BIKEHUB_VM_IP:-192.168.0.49}"
ssh_key="${BIKEHUB_SSH_KEY:-${HOME}/.ssh/truenas_admin}"
if [[ ! -r "$ssh_key" ]]; then
  printf 'SSH key is not readable: %s\n' "$ssh_key" >&2
  exit 1
fi
if [[ "$ssh_key" != /* ]]; then
  ssh_key="$PWD/$ssh_key"
fi
command -v ssh >/dev/null || { printf 'Install OpenSSH first.\n' >&2; exit 1; }

if [[ "$logs_only" == false ]]; then
  command -v ansible-playbook >/dev/null || { printf 'Install Ansible first.\n' >&2; exit 1; }
  cd -- "$repo_root"
  export ANSIBLE_LOCAL_TEMP="${ANSIBLE_LOCAL_TEMP:-${TMPDIR:-/tmp}/bikehub-ansible-local}"
  common=(
    -i "$vm_address,"
    -u ansible
    --private-key "$ssh_key"
    -e "@$repo_root/infra/ansible/inventory/group_vars/bikehub/main.yml"
    -e bikehub_target_group=all
  )

  printf '\nОбновление сервисов наблюдаемости и Grafana…\n'
  ansible-playbook "${common[@]}" "$repo_root/infra/ansible/playbooks/observability.yml"

  printf '\nВыкладка API и фронта…\n'
  ansible-playbook "${common[@]}" "$repo_root/infra/ansible/playbooks/server.yml" \
    -e "bikehub_rebuild_image=$rebuild"
fi

printf '\nСайт: https://bikehub.local/\n'
printf 'Grafana: https://truenas.local:30037/d/bikehub-api?var-environment=Production\n'
printf 'Логи API в реальном времени. Откройте сайт для новых запросов. Ctrl+C — выйти из просмотра.\n\n'
exec ssh -o BatchMode=yes -o ConnectTimeout=10 -i "$ssh_key" "ansible@$vm_address" \
  'sudo -n k3s kubectl logs -n bikehub deployment/bikehub-api --container=api --tail=200 --timestamps --follow'
