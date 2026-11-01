#!/usr/bin/env python3
"""Authorize a Gmail sender and configure BikeHub; tokens are never printed or saved locally."""
import argparse
import json
import logging
import os
from pathlib import Path
import re
import runpy
import secrets
import shlex
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
SCOPES = ["openid", "https://www.googleapis.com/auth/userinfo.email", "https://www.googleapis.com/auth/gmail.send"]


class SetupError(Exception):
    pass


def load_client(path):
    path = path.expanduser().resolve()
    if path.is_relative_to(ROOT.resolve()):
        raise SetupError("Храните JSON OAuth-клиента вне репозитория, например в Downloads.")
    if not path.is_file() or path.stat().st_size > 65_536:
        raise SetupError("Не найден JSON OAuth-клиента или файл слишком большой.")
    try:
        config = json.loads(path.read_text())
    except (OSError, ValueError):
        raise SetupError("Не удалось прочитать JSON OAuth-клиента.") from None
    installed = config.get("installed") if isinstance(config, dict) else None
    if not isinstance(installed, dict):
        raise SetupError("Нужен OAuth-клиент типа Desktop app, а не Web application.")
    if installed.get("auth_uri") not in ("https://accounts.google.com/o/oauth2/auth", "https://accounts.google.com/o/oauth2/v2/auth") or installed.get("token_uri") != "https://oauth2.googleapis.com/token":
        raise SetupError("JSON должен содержать официальные HTTPS OAuth endpoints Google.")
    if not isinstance(installed.get("client_id"), str) or not installed["client_id"].endswith(".apps.googleusercontent.com") or not isinstance(installed.get("client_secret"), str) or not installed["client_secret"]:
        raise SetupError("В JSON отсутствуют Client ID или Client secret Google.")
    return config


def canonical_email(value):
    local, domain = value.strip().lower().rsplit("@", 1)
    if domain in ("gmail.com", "googlemail.com"):
        return local.replace(".", "") + "@gmail.com"
    return local + "@" + domain


def safe_oauth_failure(error):
    """Return only a fixed diagnostic label, never OAuth response text or callback URLs."""
    known = {
        "Warning": "scope_changed", "MismatchingStateError": "state_mismatch",
        "MissingCodeError": "callback_without_code", "MissingTokenError": "token_missing",
        "AccessDeniedError": "access_denied", "InvalidGrantError": "invalid_grant",
        "InvalidClientError": "invalid_client", "InvalidScopeError": "invalid_scope",
        "SSLError": "tls_error", "ReadTimeout": "timeout", "ConnectTimeout": "timeout",
        "ConnectionError": "connection_error", "TransportError": "connection_error",
        "ValueError": "invalid_response", "InvalidValue": "invalid_response",
    }
    return known.get(type(error).__name__, "oauth_error")


def validated_credentials(client, credentials, claims, expected_sender, nonce):
    if not isinstance(claims, dict) or claims.get("email_verified") is not True or not isinstance(claims.get("email"), str):
        raise SetupError("Google не подтвердил адрес отправителя.")
    if claims.get("nonce") != nonce:
        raise SetupError("OAuth-ответ не соответствует этому запуску. Повторите привязку.")
    if canonical_email(claims["email"]) != canonical_email(expected_sender):
        raise SetupError("В Google выбран другой аккаунт. Повторите вход с ящиком отправителя.")
    granted = credentials.granted_scopes or credentials.scopes or []
    if isinstance(granted, str):
        granted = granted.split()
    if SCOPES[-1] not in granted:
        raise SetupError("Google не предоставил право отправлять письма gmail.send.")
    if not credentials.refresh_token:
        raise SetupError("Google не выдал refresh token. Повторите привязку с подтверждением доступа.")
    return {"gmail-client-id": client["installed"]["client_id"],
            "gmail-client-secret": client["installed"]["client_secret"],
            "gmail-refresh-token": credentials.refresh_token,
            "gmail-from-address": claims["email"].lower()}


def authorize(client, sender):
    try:
        from google_auth_oauthlib.flow import InstalledAppFlow, WSGITimeoutError
        from google.oauth2 import id_token
        from google.auth.transport.requests import Request
        import requests
    except ImportError:
        raise SetupError("Установите зависимости scripts/requirements-gmail-oauth.txt в отдельное Python-окружение.") from None
    # Suppress library HTTP logs, including the OAuth callback query and token response.
    logging.disable(logging.CRITICAL)

    class TimeoutAdapter(requests.adapters.HTTPAdapter):
        def send(self, request, **kwargs):
            if kwargs.get("timeout") is None:
                kwargs["timeout"] = 20
            return super().send(request, **kwargs)

    nonce = secrets.token_urlsafe(32)
    flow = InstalledAppFlow.from_client_config(client, SCOPES, autogenerate_code_verifier=True)
    flow.oauth2session.mount("https://", TimeoutAdapter(max_retries=0))
    print("Откройте ссылку в обычном браузере на этом Mac и выберите аккаунт отправителя.")
    print("Разрешения: отправка почты и подтверждение адреса аккаунта. Чтение писем не запрашивается.")
    stage = "consent"
    try:
        credentials = flow.run_local_server(host="127.0.0.1", bind_addr="127.0.0.1", port=0,
            open_browser=False, timeout_seconds=300, prompt="consent", login_hint=sender, nonce=nonce,
            authorization_prompt_message="{url}",
            success_message="Авторизация получена. Вернитесь в терминал BikeHub.")
        stage = "identity"
        with requests.Session() as session:
            session.mount("https://", TimeoutAdapter(max_retries=0))
            claims = id_token.verify_oauth2_token(credentials.id_token, Request(session=session),
                                                client["installed"]["client_id"], clock_skew_in_seconds=30)
        return validated_credentials(client, credentials, claims, sender, nonce)
    except SetupError:
        raise
    except WSGITimeoutError:
        raise SetupError("Время ожидания Google истекло (5 минут). Запустите привязку снова и откройте новую ссылку в браузере на этом Mac.") from None
    except Exception as error:
        message = ("Google OAuth не завершил выдачу токена. Проверьте ошибку в браузере, Test users и выбранный аккаунт."
                   if stage == "consent" else "Токен Google получен, но не прошёл проверку подписи и адреса отправителя.")
        raise SetupError(message + " Этап=" + stage + "; причина=" + safe_oauth_failure(error) +
                         ". Коды и токены не выводятся.") from None
    finally:
        flow.oauth2session.close()


def ssh_command(vm, key, program):
    return ["ssh", "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "-i", str(key),
            "ansible@" + vm, "sudo -n python3 -c " + shlex.quote(program)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--client-json", type=Path, help="Downloaded Desktop app OAuth client JSON, outside the repository")
    parser.add_argument("--sender", help="Gmail/Workspace address that sends BikeHub codes")
    parser.add_argument("--target", choices=("server", "local"), default="server")
    parser.add_argument("--check", action="store_true", help="Verify saved server token refresh without sending email")
    parser.add_argument("--vm-ip", default=os.environ.get("BIKEHUB_VM_IP", "192.168.0.49"))
    parser.add_argument("--ssh-key", type=Path, default=Path(os.environ.get("BIKEHUB_SSH_KEY", "~/.ssh/truenas_admin")))
    args = parser.parse_args()
    key = args.ssh_key.expanduser().resolve()
    if args.check and args.target != "server":
        parser.error("--check is available for --target server.")
    if args.target == "server":
        if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.-]*", args.vm_ip) or not key.is_file():
            parser.error("Check the VM hostname/IP and SSH key path.")
        # Fail before consent if the VM cannot be reached. Do not request a token that cannot be stored.
        preflight = subprocess.run(["ssh", "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "-i", str(key),
                                   "ansible@" + args.vm_ip, "sudo -n k3s kubectl get namespace bikehub -o name"],
                                  text=True, capture_output=True, timeout=25)
        if preflight.returncode:
            raise SetupError("Нет SSH/sudo-доступа к K3s на VM. Проверьте его перед привязкой OAuth.")
    payload = {"namespace": "bikehub", "name": "bikehub-auth", "action": "check-gmail" if args.check else "configure-gmail"}
    if not args.check:
        if not args.client_json:
            parser.error("Pass --client-json with the downloaded Desktop app client file.")
        client = load_client(args.client_json)
        sender = (args.sender or input("Адрес ящика отправителя Gmail: ")).strip().lower()
        if len(sender) > 254 or not re.fullmatch(r"[^\s@]+@[^\s@]+\.[^\s@]+", sender):
            parser.error("Enter the sender email address.")
        payload["credentials"] = authorize(client, sender)
    program_path = ROOT / "infra/ansible/scripts/configure-auth-secret.py"
    if args.target == "local":
        check = runpy.run_path(str(program_path))["check_gmail_oauth"](payload["credentials"])
        if not check["ok"]:
            raise SetupError("Обновление OAuth-токена не прошло: " + check["kind"])
        mapping = {"gmail-client-id": "ClientId", "gmail-client-secret": "ClientSecret",
                   "gmail-refresh-token": "RefreshToken", "gmail-from-address": "FromAddress"}
        settings = {"Authentication:EmailProvider": "GmailApi", **{
            "GmailApi:" + name: payload["credentials"][field] for field, name in mapping.items()}}
        subprocess.run(["dotnet", "user-secrets", "set", "--project", str(ROOT / "src/BikeHub.Api")],
                       input=json.dumps(settings), text=True, check=True, capture_output=True)
        print("Gmail OAuth сохранён в локальных .NET user-secrets. Перезапустите API в Development.")
        return 0
    response = subprocess.run(ssh_command(args.vm_ip, key, program_path.read_text()),
                              input=json.dumps(payload), text=True, capture_output=True, timeout=90)
    if response.returncode:
        raise SetupError("Не удалось проверить или сохранить OAuth на VM. Проверьте SSH/sudo; токены не выводятся.")
    try:
        result = json.loads(response.stdout)
        oauth = result["oauth"]
    except (ValueError, KeyError, TypeError):
        raise SetupError("VM вернула неожиданный ответ. Его содержимое скрыто.") from None
    if not oauth.get("ok"):
        print("Gmail OAuth: отказ; причина=" + oauth.get("kind", "unknown") + "; HTTP=" + str(oauth.get("status", "—")))
        return 1
    print("Gmail OAuth: токен успешно обновляется с VM. Проверочное письмо не отправлялось.")
    if not args.check:
        print("OAuth сохранён в bikehub/bikehub-auth; выбран GmailApi, прежний OTP-секрет сохранён.")
        print("Раскатить API и применить настройки: bash scripts/redeploy.sh --enable-auth")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SetupError as error:
        raise SystemExit(str(error)) from None
    except (KeyboardInterrupt, EOFError):
        raise SystemExit("\nПривязка прервана.") from None
    except Exception:
        raise SystemExit("Не удалось завершить привязку Gmail. Подробности скрыты, чтобы не вывести OAuth-секреты.") from None
