# Вход и подтверждение почты

Первый вход и регистрация: почта → шестизначный код → подтверждённый аккаунт и сессия. Пароля BikeHub нет. Повторный вход с той же почтой открывает прежний аккаунт; регистр адреса не учитывается. Имя используется только при создании аккаунта, почта не попадает в публичный профиль.

## Локальная разработка

```bash
# API
dotnet run --project src/BikeHub.Api --launch-profile http
# Фронт, в другом терминале
npm --prefix Web ci
npm --prefix Web run dev
```

Откройте http://localhost:5173 и войдите с тестовым адресом. На шаге кода доступны «Посмотреть тестовое письмо» и «Вставить». Development использует память; тестовое письмо доступно только браузеру, запросившему код, и только при loopback-соединении. В production тестовая почта запрещена.

## Конфигурация

Основной API хранит параметры входа в `Authentication`. `Authentication:EmailProvider=Development` включает локальную тестовую почту, `MailService` — HTTP-клиент отдельного отправителя. Его подключение: `MailService:BaseUrl` и `MailService:ApiKey`.

У `BikeHub.Mail` транспорт выбирается через `Mail:Provider=Smtp/GmailApi`. Секции `Smtp` и `GmailApi` находятся на верхнем уровне конфигурации **почтового сервиса**. Для user-secrets применяются ключи `Smtp:Username`, `Smtp:Password`, `GmailApi:RefreshToken`; для окружения — `Smtp__Username`, `Smtp__Password`, `GmailApi__RefreshToken`.

При переносе прежних локальных настроек запишите SMTP/Gmail-значения в user-secrets `src/BikeHub.Mail`. Старый `Authentication:EmailProvider=Smtp/GmailApi` в user-secrets API удалите или замените на `MailService` с адресом и API-ключом сервиса. На NAS существующий `bikehub-auth` сохраняется, токены заново выдавать не нужно. Ansible подключает почтовые ключи только к `bikehub-mail`, OTP-ключ — к API. Запуск сервисов и асинхронная доставка описаны [отдельно](mail-service.md).

## Настройка SMTP

Для Gmail нужен [пароль приложения Google](https://support.google.com/accounts/answer/185833?hl=ru), обычный пароль аккаунта не подходит. Единая .NET CLI запрашивает пароль со скрытым вводом и проверяет TLS/AUTH без отправки письма:

```bash
# Локальные user-secrets BikeHub.Mail
dotnet run --project tools/BikeHub.Tools -- mail smtp --target local

# Secret в существующей VM
dotnet run --project tools/BikeHub.Tools -- mail smtp --target server
```

Используется `smtp.gmail.com:587` с обязательным STARTTLS и проверкой сертификата. При отказе настройки не сохраняются. User-secrets — локальный файл вне репозитория, а не зашифрованное хранилище. Не копируйте его в Docker-образ. Другие SMTP можно настроить через `Smtp:*`; поддерживаются `StartTls` и `SslOnConnect`.

## Gmail OAuth2

1. Создайте проект в [Google Cloud Console](https://console.cloud.google.com/) и включите Gmail API.
2. В Google Auth platform настройте Branding/Audience. При Testing добавьте отправителя в Test users.
3. Добавьте scopes: `openid`, `https://www.googleapis.com/auth/userinfo.email`, `https://www.googleapis.com/auth/gmail.send`.
4. Создайте OAuth Client типа Desktop app и скачайте JSON **вне репозитория**.

```bash
dotnet run --project tools/BikeHub.Tools -- mail gmail \
  --client-json "$HOME/Downloads/BikeHub OAuth Client ID.json" \
  --sender sender@gmail.com --target server
```

Для локальных user-secrets укажите `--target local`. Откройте напечатанную ссылку в браузере на том же компьютере. Используется официальный Google .NET SDK с PKCE, случайными state/nonce и callback на loopback со случайным портом. Проверяются подпись, issuer, audience и срок ID token, подтверждённый адрес, nonce, совпадение выбранного аккаунта, scope `gmail.send` и наличие refresh token. Доступ к чтению писем не запрашивается.

Токены не печатаются и не сохраняются в `token.json`. Для сервера CLI передаёт настройки Ansible через stdin; playbook проверяет refresh с VM перед записью Secret. Для локальной настройки используется stdin `dotnet user-secrets set`. Пароли и токены не попадают в аргументы процессов. SDK почтового сервиса обновляет access token в памяти, без файлового DataStore.

Для Testing учитывайте [условия истечения refresh token Google](https://developers.google.com/identity/protocols/oauth2#expiration), включая семидневный срок Gmail-грантов. Проверяйте [требования к scopes и верификации](https://developers.google.com/workspace/gmail/api/auth/scopes) перед постоянным запуском.

## Серверные Secrets и развёртывание

.NET CLI использует `infra/ansible/playbooks/configure-mail.yml`. Он работает в существующей VM через `ansible@192.168.0.49` и `~/.ssh/truenas_admin`. Переопределения: `--vm-address IP`, `--ssh-key PATH`. Если образ почтового сервиса ещё отсутствует, playbook сначала собирает образы без выкладки приложения.

SMTP/Gmail проверяется с VM в контейнере `BikeHub.Mail --check-transport --configuration-stdin`, без отправки. Отказ не изменяет Secret. У существующего Secret читаются только revision и наличие OTP-ключа; merge patch с `resourceVersion` сохраняет его и посторонние ключи. OTP генерируется только при отсутствии. Пароли передаются через SSH stdin; playbook включает SSH pipelining и `no_log`, без промежуточных файлов конфигурации с введёнными секретами. Подключение к PostgreSQL не читается и не меняется.

Ключи `bikehub-auth`: `otp-secret`; для SMTP — `smtp-username`, `smtp-password`, `smtp-from-address`; для Gmail — `gmail-client-id`, `gmail-client-secret`, `gmail-refresh-token`, `gmail-from-address`. Транспорт задаётся аннотацией `bikehub.io/email-provider`.

Проверка уже настроенного транспорта без чтения значений Secret на контроллер и без отправки письма:

```bash
dotnet run --project tools/BikeHub.Tools -- mail check --target server
```

Playbook выполняет `--check-transport` внутри работающего mail Pod с его текущим окружением. Новую конфигурацию Secrets нужно применить обычной выкладкой, прежде чем проверять её этим способом.

```bash
ansible-playbook -i '192.168.0.49,' -u ansible \
  --private-key ~/.ssh/truenas_admin infra/ansible/playbooks/server.yml \
  -e @infra/ansible/inventory/group_vars/bikehub/main.yml \
  -e bikehub_target_group=all -e bikehub_auth_enabled=true
```

Ansible по умолчанию сохраняет текущий режим авторизации (`bikehub_auth_enabled: null`). `true` и `false` явно его меняют; на новой установке по умолчанию вход выключен.

В production нужны PostgreSQL, стабильный OTP-секрет, постоянные ключи cookie, реальный почтовый транспорт, общий внутренний API-ключ и HTTPS. У основного API задаются `Authentication__Store=Postgres`, `Authentication__EmailProvider=MailService`, `MailService__BaseUrl`, `MailService__ApiKey`. Ansible хранит cookie-ключи в PVC, подключает Secrets через `secretKeyRef`, обновляет Pod по revision. При TLS на прокси указываются доверенные `ReverseProxy:KnownProxies`; прямой NodePort должен оставаться закрытым для внешнего доступа.

Email-подтверждение не подтверждает номер телефона и само по себе не означает выполнения правовых требований к сервису.

## API и защита

| Метод | Путь | Поведение |
| --- | --- | --- |
| GET | `/api/auth/session` | Текущий пользователь или `null`, флаги подтверждения и CSRF-токен; `Cache-Control: no-store` |
| POST | `/api/auth/email/challenges` | `{ email, name? }` → 202 с `challengeId`, `expiresAt`, `resendAt` |
| POST | `/api/auth/email/verify` | `{ challengeId, code }` → подтверждённый пользователь, новая сессия и CSRF-токен |
| POST | `/api/auth/logout` | Отзыв текущей сессии, удаление cookie, новый гостевой CSRF-токен |
| GET | `/api/auth/development/messages/{challengeId}` | Тестовое письмо только в локальном Development, с cookie привязки браузера |

Клиент сохраняет cookie и передаёт `X-CSRF-TOKEN` во **всех POST/PUT/DELETE**, включая запрос кода, проверку и выход. Токен берётся из `/auth/session`, обновляется из ответа входа/выхода. Web делает это автоматически. JWT в localStorage не используется.

Код одноразовый, действует 5 минут, допускает 5 неверных попыток. Повторный запрос возможен через 60 секунд, до 5 раз в час для адреса. Новый код заменяет прежний код для той же почты или браузера. Проверка и создание сессии атомарны; в БД хранятся HMAC кода и привязки браузера, а не открытый код. IP-лимиты: 10 запросов отправки и 30 проверок в минуту на процесс. Суточный бюджет и ограничения почты общие для реплик через PostgreSQL.

Сессия действует 14 дней без скользящего продления, проверяется в хранилище при каждом запросе с cookie. Выход отзывает её на сервере. В production cookie имеют `Secure`, `HttpOnly`, префикс `__Host-`; применяются SameSite и CSRF. Коды и адреса не добавляются в сообщения журналирования и атрибуты метрик авторизации; API журналирует тип ошибки обращения к почтовому сервису; сервис журналирует безопасную категорию ошибки транспорта. Метрика `bikehub.auth.events` экспортируется в существующий OpenTelemetry Collector с результатом операции, без адресов и кодов.

Гостю доступны публичные лента, велосипеды, профили и комментарии. Запись, комментарий, реакция, подписка и вступление в сообщество требуют подтверждённой почты. `/users/me`, личные закладки и лента подписок возвращают 401 гостю. Владельца велосипеда и автора записи сервер берёт из сессии.

## Задел под SMS

`ISmsSender` отделяет будущий SMS-транспорт. `TestSmsSender` сохраняет тестовые сообщения в памяти только в Development и ничего не отправляет. В production он отклоняет вызовы. В схеме аккаунта оставлены nullable `phone_e164` и `phone_confirmed_at`. Телефонные endpoint, привязка номера и реальное SMS-подтверждение пока не включены; `phoneConfirmed` возвращается `false`.

## Проверка

```bash
dotnet test BikeHub.sln --disable-build-servers
ansible-playbook -i localhost, infra/tests/configure-mail.yml
```

xUnit проверяет авторизацию и сквозной сценарий сообщества; Testcontainers автоматически поднимает одноразовые PostgreSQL и Kafka, применяет миграции и удаляет контейнеры. Нужен Docker. Рабочие подключения не используются. Ansible-тест с локальными заглушками проверяет сохранение OTP, генерацию отсутствующего ключа и отсутствие записи при отклонённых настройках. Реальные письма эти проверки не отправляют.
