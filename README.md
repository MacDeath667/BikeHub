# BikeHub

Велосообщество с механикой Drive2: у владельца есть гараж, у велосипеда — карточка и бортжурнал, записи попадают в общую ленту и ленту подписок.

## Быстрый старт

Нужны .NET 10 и Node.js 22.12+. Из корня репозитория:

```bash
# Терминал 1 — API
dotnet run --project src/BikeHub.Api --launch-profile http

# Терминал 2 — фронт
npm --prefix Web ci
npm --prefix Web run dev
```

Интерфейс: http://localhost:5173. Swagger: http://localhost:5179/swagger. Vite проксирует `/api` на `127.0.0.1:5179`.
Development использует память и тестовое письмо в форме входа. Для запуска API без экспорта телеметрии задайте `Observability__Enabled=false`.

Для локальной PostgreSQL и проверки EF:

```bash
docker compose up -d --wait postgres
ConnectionStrings__BikeHub='Host=127.0.0.1;Port=55432;Database=bikehub_local;Username=bikehub_local;Password=bikehub_local' \
Authentication__Store=Postgres Database__MigrationsEnabled=true \
dotnet run --project src/BikeHub.Api --launch-profile http
```

Это отдельная база с локальными учётными данными только для разработки. Аккаунты сохраняются в Docker volume. `docker compose down` останавливает её без удаления данных.

## Запуск на NAS

С Mac/Linux с установленным Ansible и ключом `~/.ssh/truenas_admin`:

```bash
ansible-playbook -i '192.168.0.49,' -u ansible \
  --private-key ~/.ssh/truenas_admin \
  infra/ansible/playbooks/server.yml \
  -e @infra/ansible/inventory/group_vars/bikehub/main.yml \
  -e bikehub_target_group=all -e bikehub_auth_enabled=true
```

Playbook собирает фронт и API в существующей VM, обновляет сбор телеметрии, применяет миграции перед обновлением API и проверяет готовность сайта. `bikehub_auth_enabled=true` оставляет вход включённым; почтовые настройки берутся из существующего Secret. Сайт: [https://bikehub.local/](https://bikehub.local/). Подробности — в [инструкции TrueNAS](infra/README.md).

```bash
ssh -i ~/.ssh/truenas_admin ansible@192.168.0.49 \
  'sudo -n k3s kubectl logs -n bikehub deployment/bikehub-api --container=api --tail=200 --follow'
```

Для применения конфигов с текущим образом добавьте к команде Ansible `-e bikehub_rebuild_image=false`. Для отдельного обновления Grafana используйте `infra/ansible/playbooks/observability.yml` с теми же параметрами подключения.

## Первая версия

- Лента «Для вас», «Подписки», «Популярное», поиск и фильтры по типу велосипеда.
- Каталог велосипедов, личный гараж, карточка велосипеда и бортжурнал.
- Профили владельцев и подписки.
- Создание велосипеда и публикация записи в его бортжурнале.
- Лайки, закладки, комментарии и вступление в сообщества.
- Адаптивный интерфейс, мобильное меню, состояния загрузки, ошибок и пустых списков.

Вход и регистрация — по почте и одноразовому коду, без пароля. Гость может читать, а личный гараж, реакции и публикации доступны после подтверждения почты. В Development тестовое письмо открывается прямо в форме, настоящая почта не отправляется. Для реальной отправки поддержаны SMTP и Gmail API с OAuth2. Настройка Gmail, серверной авторизации и тестового SMS-провайдера описана в [инструкции авторизации](docs/authentication.md).

Реальная отправка вынесена в отдельный `BikeHub.Mail`: HTTP API, Kafka consumer, очередь PostgreSQL и общая идемпотентность по `messageId`. Запуск, контракты и границы гарантий описаны в [инструкции почтового сервиса](docs/mail-service.md).

Аккаунты и сессии поддерживают PostgreSQL; локально по умолчанию используется память. Контент сообщества пока работает через моки в памяти процесса: велосипеды, записи, комментарии и реакции сбрасываются при перезапуске, реплики не синхронизированы. Загрузка собственных фотографий ещё не реализована; обложка новой записи берётся из карточки велосипеда.

## Структура

- `Web` — React, TypeScript, Vite; страницы и общие компоненты.
- `src/BikeHub.Application/Community` — DTO, валидация входных данных и интерфейсы пяти сервисов.
- `src/BikeHub.Api/Controllers` — HTTP-контроллеры.
- `src/BikeHub.Api/Services` — реализации моков и начальные данные.
- `src/BikeHub.Mail` — почтовый микросервис с HTTP API, Kafka consumer и транспортами SMTP/Gmail.
- `src/BikeHub.Mail.Contracts` — общий контракт письма и статусов для производителей и почтового сервиса.

Сервисы зарегистрированы через DI: при подключении реального хранилища можно заменить реализации интерфейсов без изменения HTTP-контракта. DAL использует EF Core и PostgreSQL, контекст создаётся через `IDbContextFactory<BikeHubDbContext>` на каждую операцию. В `Development` bootstrap и миграции отключены; `--migrate-only` запускает EF migrations явно.

Миграции и переход с прежней схемы DbUp описаны в [инструкции хранилища](docs/persistence.md).

## Grafana

В Development API отправляет метрики, логи и трассировки через OpenTelemetry в Collector на `http://bikehub.local:30418`.
Дашборд — [BikeHub · API](https://truenas.local:30037/d/bikehub-api), фильтр окружения — `Development` или `Production`.
Первоначальная настройка и проверка описаны в [инструкции наблюдаемости](infra/observability/README.md).
Чтобы запустить API без экспорта, задайте `Observability__Enabled=false`.

## Сборка и проверка

Повторяющиеся смысловые значения бэкенда храним в `Constants`, разовые строки — в приватных константах класса. Маршруты исключены из этого правила: шаблоны и пути прописываем литералами в месте использования, даже при повторении. Для моковых данных, строк в атрибутах валидации, простых форматов и стандартных значений допустимы литералы, если они читаются яснее.

Имена SQL-параметров также оставляем литералами в запросах и при привязке значений (`AddWithValue("id", id)`), без общих или приватных констант.

У `if` и `else` обязательны фигурные скобки, включая однострочные ветки. Правило Roslyn `IDE0011` настроено в `.editorconfig` как ошибка и проверяется при сборке всех C#-проектов, в том числе в Docker. Оно также требует скобки у циклов, `using` и `lock`.

```bash
dotnet build BikeHub.sln --disable-build-servers
dotnet test BikeHub.sln --disable-build-servers
cd Web
npm run build
```

Проверить правило скобок отдельно или применить автоисправление из корня:

```bash
dotnet format style BikeHub.sln --diagnostics IDE0011 --verify-no-changes --no-restore
dotnet format style BikeHub.sln --diagnostics IDE0011 --no-restore
```

`dotnet test` сам поднимает одноразовые PostgreSQL и Kafka через Testcontainers и удаляет их после проверки. Нужен работающий Docker; рабочие БД не используются. Сквозной сценарий входа, гаража, публикаций и реакций находится в `CommunityFlowTests` и запускается без внешнего API.

.NET CLI для привязки Gmail и сквозной проверки телеметрии:

```bash
dotnet run --project tools/BikeHub.Tools -- --help
```

## Docker

Весь локальный стенд запускается из одного `compose.yaml`:

```bash
docker compose up --build --wait
```

Поднимаются API с готовым фронтом и nginx, отдельный почтовый сервис, один PostgreSQL с базами API и почты, Kafka, создание топика и Mailpit для просмотра писем. Почтовый сервис автоматически создаёт базу `bikehub_mail`, если её ещё нет. Миграции обоих сервисов применяются автоматически. API использует почтовый сервис и постоянное хранилище аккаунтов; Kafka consumer включён. Для запуска не нужны Gmail, Ansible или доступ к NAS.

| Компонент | Локальный адрес |
| --- | --- |
| Сайт | http://localhost:8080 |
| Swagger основного API | http://localhost:8080/swagger |
| Письма и коды входа в Mailpit | http://localhost:8025 |
| API почтового сервиса / Swagger | http://localhost:5181/swagger |
| PostgreSQL API | localhost:55432, БД/логин/пароль `bikehub_local` |
| PostgreSQL почты | localhost:55432, БД `bikehub_mail`, логин/пароль `bikehub_local` |
| Kafka | localhost:59092, топик `bikehub.mail.send.v1` |
| SMTP Mailpit | localhost:11025 |

Для входа запросите код на сайте и откройте письмо в Mailpit. Письмо проходит через HTTP API почтового сервиса, PostgreSQL-очередь и SMTP. Mailpit перехватывает письма локально; настоящие адресаты их не получают. Порты опубликованы только на loopback. Локальные пароли и API-ключ в Compose предназначены для разработки.

```bash
docker compose logs -f bikehub.api mail
docker compose down
```

`down` сохраняет базы, Kafka, письма и cookie-ключи в volumes. Порты можно переопределить переменными `BIKEHUB_HTTP_PORT`, `BIKEHUB_MAIL_PORT`, `BIKEHUB_MAILPIT_PORT`, `BIKEHUB_POSTGRES_PORT`, `BIKEHUB_KAFKA_PORT`, `BIKEHUB_SMTP_PORT`.

При переходе с прежнего отдельного контейнера БД почты его volume сохраняется. Переключение на общий сервер не переносит его данные автоматически; существующие volumes не удаляйте, если они нужны для переноса.

Для разработки через Rider/`dotnet run` можно поднять только инфраструктуру тем же файлом:

```bash
docker compose up -d --wait postgres kafka-topic mailpit
```

Запуск почты вне Docker описан в [инструкции сервиса](docs/mail-service.md). Для локальной раздачи production-сборки можно скопировать содержимое `Web/dist` в `src/BikeHub.Api/wwwroot`; генерируемые файлы не коммитятся.
