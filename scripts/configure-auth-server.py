#!/usr/bin/env python3
"""Configure Gmail for the existing BikeHub VM using SSH stdin, without local secret files."""
import argparse
import getpass
import runpy
import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def load_smtp_checker():
    return runpy.run_path(str(ROOT / "infra/ansible/scripts/configure-auth-secret.py"))["check_smtp"]


def print_smtp_result(origin, result):
    if result.get("ok"):
        print(f"{origin}: SMTP-вход принят (235).")
    else:
        print(f"{origin}: этап={result.get('stage', 'configuration')}; код={result.get('status', '—')}; статус={result.get('enhanced') or '—'}; причина={result.get('kind', 'unknown')}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    operation = parser.add_mutually_exclusive_group()
    operation.add_argument("--check", action="store_true", help="Check required key presence without changing or displaying values")
    operation.add_argument("--test-smtp", action="store_true", help="Test Gmail TLS and login with saved credentials; never send mail")
    operation.add_argument("--diagnose-smtp", action="store_true", help="Compare the same newly entered credentials from Mac and VM; never save them or send mail")
    parser.add_argument("--vm-ip", default=os.environ.get("BIKEHUB_VM_IP", "192.168.0.49"))
    parser.add_argument("--ssh-key", type=Path, default=Path(os.environ.get("BIKEHUB_SSH_KEY", "~/.ssh/truenas_admin")).expanduser())
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.-]*", args.vm_ip):
        parser.error("Use the VM IPv4 address or hostname, without a user or SSH options.")
    args.ssh_key = args.ssh_key.expanduser()
    if not args.ssh_key.is_file():
        parser.error("SSH key file was not found.")
    payload = {"action": "check" if args.check else "test-smtp" if args.test_smtp else "test-input" if args.diagnose_smtp else "configure", "namespace": "bikehub", "name": "bikehub-auth"}
    if not args.check and not args.test_smtp:
        if not sys.stdin.isatty():
            parser.error("Run in an interactive terminal so the app password can be entered without echo.")
        print(f"Gmail → Kubernetes Secret bikehub/bikehub-auth на VM {args.vm_ip}.")
        if args.diagnose_smtp:
            print("Проверим один ввод с Mac и VM. Настройки сохраняться не будут; письмо не отправляется.")
        else:
            print("Сначала проверим вход в Gmail. После успешной проверки сохраним SMTP, сохранив прежний OTP-секрет.")
        payload["email"] = input("Адрес личного Gmail: ").strip().lower()
        if not re.fullmatch(r"[^\s@]+@(gmail|googlemail)\.com", payload["email"]):
            parser.error("Enter a personal Gmail address.")
        # Fail closed if Python cannot disable echo; getpass's fallback would reveal the password.
        import warnings
        with warnings.catch_warnings():
            warnings.simplefilter("error", getpass.GetPassWarning)
            try:
                payload["password"] = getpass.getpass("Пароль приложения Google (ввод скрыт): ").replace(" ", "")
            except getpass.GetPassWarning:
                parser.error("Could not disable terminal echo; no password was read.")
        if not re.fullmatch(r"[A-Za-z]{16}", payload["password"]):
            parser.error("Expected a 16-character Google app password, not the Google account password.")

    local_result = None
    if args.diagnose_smtp:
        local_result = load_smtp_checker()(payload["email"], payload["password"])
        print_smtp_result("Mac", local_result)

    program = (ROOT / "infra/ansible/scripts/configure-auth-secret.py").read_text()
    command = ["ssh", "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "-i", str(args.ssh_key.expanduser().resolve()),
               "ansible@" + args.vm_ip, "sudo -n python3 -c " + shlex.quote(program)]
    try:
        response = subprocess.run(command, input=json.dumps(payload), text=True, capture_output=True, timeout=90)
    except (OSError, subprocess.TimeoutExpired):
        raise SystemExit("Не удалось выполнить SSH-команду. Проверьте доступ к VM; затем запустите --check.") from None
    if response.returncode:
        # Do not forward output from either SSH or kubectl: never risk repeating a submitted credential.
        raise SystemExit(f"Настройка не выполнена (SSH exit {response.returncode}). Проверьте доступ к ansible@{args.vm_ip} и sudo -n; подробности не выводятся, чтобы не раскрыть секреты.")
    try:
        result = json.loads(response.stdout)
        if not isinstance(result, dict) or not isinstance(result.get("ready"), bool):
            raise ValueError()
    except (ValueError, TypeError):
        raise SystemExit("VM вернула неожиданный ответ; запустите --check. Содержимое ответа скрыто.") from None
    if args.check:
        print("Secret bikehub/bikehub-auth: " + ("все ключи присутствуют; SMTP не проверялся" if result["ready"] else "не настроен полностью"))
        for name in ("otp-secret", "smtp-username", "smtp-password", "smtp-from-address"):
            print(f"  {name}: {'есть' if result.get('keys', {}).get(name) else 'нет'}")
        return 0 if result["ready"] else 1
    if args.diagnose_smtp:
        print_smtp_result("VM", result.get("smtp", {}))
        print("Проверен один и тот же ввод. Secret и работающий сайт не изменены.")
        return 0 if local_result["ok"] and result.get("smtp", {}).get("ok") else 1
    smtp = result.get("smtp", {})
    if not smtp.get("ok"):
        messages = {"auth_rejected": "Gmail отклонил логин и пароль приложения", "tls_error": "не прошла проверка TLS",
                    "timeout": "истекло время ожидания", "connection_error": "нет соединения с Gmail",
                    "missing_configuration": "сначала настройте SMTP Secret", "protocol_error": "сбой SMTP-протокола",
                    "smtp_rejected": "SMTP-сервер отклонил команду"}
        print("SMTP: " + messages.get(smtp.get("kind"), "проверка не прошла"))
        print(f"Этап: {smtp.get('stage', 'configuration')}; код: {smtp.get('status', '—')}; статус: {smtp.get('enhanced') or '—'}")
        if smtp.get("kind") == "auth_rejected":
            print("Проверьте аккаунт, где создан пароль приложения: https://myaccount.google.com/apppasswords")
        if not args.test_smtp:
            print("Secret не изменён: SMTP-проверка завершилась ошибкой.")
        return 1
    print("SMTP: TLS и вход в Gmail работают. Письмо не отправлялось.")
    if args.test_smtp:
        return 0
    print("Secret bikehub/bikehub-auth сохранён. Значения не записаны в локальные файлы и не выведены.")
    print("OTP-секрет: " + ("создан" if result.get("otpGenerated") else "сохранён прежний"))
    print("Для раскатки с авторизацией: bash scripts/redeploy.sh --enable-auth")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (KeyboardInterrupt, EOFError):
        raise SystemExit("\nНастройка прервана.") from None
