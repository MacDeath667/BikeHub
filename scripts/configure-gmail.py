#!/usr/bin/env python3
"""Configure local Development SMTP using .NET user-secrets. Never commits or prints credentials."""
import getpass
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]

def main():
    print("Включите двухэтапную проверку Google и создайте пароль приложения для BikeHub:")
    print("https://support.google.com/accounts/answer/185833?hl=ru")
    email = input("Адрес личного Gmail: ").strip().lower()
    if not re.fullmatch(r"[^\s@]+@(gmail|googlemail)\.com", email):
        raise SystemExit("Укажите адрес личного Gmail. Другой SMTP можно настроить вручную по инструкции.")
    password = getpass.getpass("Пароль приложения Google (ввод скрыт): ").replace(" ", "")
    if not re.fullmatch(r"[A-Za-z]{16}", password):
        raise SystemExit("Нужен 16-символьный пароль приложения, а не обычный пароль Google.")
    settings = {
        "Authentication:EmailProvider": "Smtp",
        "Smtp:Host": "smtp.gmail.com",
        "Smtp:Port": "587",
        "Smtp:Security": "StartTls",
        "Smtp:Username": email,
        "Smtp:Password": password,
        "Smtp:FromAddress": email,
        "Smtp:FromName": "BikeHub",
    }
    subprocess.run(["dotnet", "user-secrets", "set", "--project", str(ROOT / "src/BikeHub.Api")],
                   input=json.dumps(settings), text=True, check=True, stdout=subprocess.DEVNULL)
    print("SMTP сохранён в локальных .NET user-secrets. Перезапустите API в Development.")
    print("Теперь запрос кода отправит настоящее письмо. Не публикуйте user-secrets.")
    print("Вернуть тестовую почту: dotnet user-secrets remove Authentication:EmailProvider --project src/BikeHub.Api")

if __name__ == "__main__":
    main()
