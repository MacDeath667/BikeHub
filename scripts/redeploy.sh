#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
logs_only=false
rebuild=true
auth_mode=preserve

usage() {
  cat <<'USAGE'
Usage: scripts/redeploy.sh [--logs-only] [--no-build] [--enable-auth | --disable-auth]

Updates Grafana provisioning and deploys BikeHub, then follows API logs.
  --logs-only  Follow API logs without deploying.
  --no-build   Apply configuration using the current image without rebuilding.
  --enable-auth   Enable email authentication (requires bikehub-auth Secret).
  --disable-auth  Disable authentication; leave public reading available.
  By default, preserve the deployed Authentication__Enabled value.
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
    --enable-auth)
      [[ "$auth_mode" != disable ]] || { printf 'Choose only one authentication option.\n' >&2; exit 2; }
      auth_mode=enable ;;
    --disable-auth)
      [[ "$auth_mode" != enable ]] || { printf 'Choose only one authentication option.\n' >&2; exit 2; }
      auth_mode=disable ;;
    -h|--help) usage; exit 0 ;;
    *) printf 'Unknown argument: %s\n' "$argument" >&2; usage >&2; exit 2 ;;
  esac
done

if [[ "$logs_only" == true && "$auth_mode" != preserve ]]; then
  printf 'Authentication options cannot be combined with --logs-only.\n' >&2
  exit 2
fi

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

  case "$auth_mode" in
    enable) auth_enabled=true ;;
    disable) auth_enabled=false ;;
    preserve)
      auth_enabled="$(ssh -o BatchMode=yes -o ConnectTimeout=10 -i "$ssh_key" "ansible@$vm_address" \
        "sudo -n k3s kubectl get deployment bikehub-api -n bikehub --ignore-not-found -o 'jsonpath={.spec.template.spec.containers[0].env[?(@.name==\"Authentication__Enabled\")].value}'")"
      case "$auth_enabled" in
        true|false) ;;
        '') auth_enabled=false ;;
        *) printf 'Cannot determine deployed authentication mode; use --enable-auth or --disable-auth.\n' >&2; exit 1 ;;
      esac ;;
  esac

  printf '\nОбновление сервисов наблюдаемости и Grafana…\n'
  ansible-playbook "${common[@]}" "$repo_root/infra/ansible/playbooks/observability.yml"

  printf '\nВыкладка API и фронта…\n'
  ansible-playbook "${common[@]}" "$repo_root/infra/ansible/playbooks/server.yml" \
    -e "bikehub_rebuild_image=$rebuild" -e "bikehub_auth_enabled=$auth_enabled"
fi

printf '\nСайт: https://bikehub.local/\n'
printf 'Grafana: https://truenas.local:30037/d/bikehub-api?var-environment=Production\n'
printf 'Логи API в реальном времени. Откройте сайт для новых запросов. Ctrl+C — выйти из просмотра.\n\n'
exec ssh -o BatchMode=yes -o ConnectTimeout=10 -i "$ssh_key" "ansible@$vm_address" \
  'sudo -n k3s kubectl logs -n bikehub deployment/bikehub-api --container=api --tail=200 --timestamps --follow'
