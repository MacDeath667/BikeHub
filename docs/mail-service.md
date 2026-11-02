# Почтовый сервис BikeHub.Mail

`BikeHub.Mail` — отдельный процесс и Docker-образ. SMTP и Gmail API с OAuth2 находятся только здесь. Основной API формирует текст кода и вызывает HTTP API почтового сервиса. Другие отправители могут публиковать тот же контракт в Kafka. Доставка асинхронная: HTTP 202 означает, что письмо надёжно принято в очередь, а не что почтовый сервер уже принял его.

На NAS доступен [отдельный staging](../infra/README.md#staging-рядом-с-основным-инстансом) с собственной очередью в схеме PostgreSQL `staging`, Kafka и Mailpit. Он использует тестовую доставку без Gmail-учётных данных; письма видны на `https://bikehub-staging.local/mailpit/`.

## Локальный запуск

Весь стенд, включая почту и Kafka, описан в основном `compose.yaml`:

```bash
docker compose up --build --wait
```

Сайт: http://localhost:8080; HTTP API/Swagger почты: http://localhost:5181/swagger; письма и коды входа в Mailpit: http://localhost:8025. SMTP направлен в Mailpit, настоящая почта не отправляется. Включены очередь PostgreSQL, Kafka consumer и автоматические миграции.

Для запуска почтового процесса через .NET поднимите только зависимости:

```bash
docker compose up -d --wait postgres kafka-topic mailpit
Mail__Provider=Smtp Smtp__Host=127.0.0.1 Smtp__Port=11025 Smtp__Security=None \
Smtp__FromAddress=no-reply@bikehub.local Smtp__Username='' Smtp__Password='' \
dotnet run --project src/BikeHub.Mail --launch-profile http
```

API по-прежнему доступен на `127.0.0.1:5181`, Kafka — `127.0.0.1:59092`, общий сервер PostgreSQL — `127.0.0.1:55432` (логин/пароль `bikehub_local`, базы `bikehub_local` и `bikehub_mail`). База почты создаётся при первом старте почтового сервиса. `Smtp:Security=None` и отсутствие SMTP-auth разрешены только в Development, без любых SMTP-учётных данных. В production требуются проверяемый TLS и аутентификация. `Mail:Provider=Development` остаётся доступен для сухого прогона, который не доставляет письмо даже в Mailpit.

Для настоящего письма настройте `Smtp:*` либо `GmailApi:*` в user-secrets **проекта BikeHub.Mail** и выберите `Mail:Provider=Smtp` либо `GmailApi`. Единая .NET CLI проверяет настройки и сохраняет их в новый проект:

```bash
dotnet run --project tools/BikeHub.Tools -- mail smtp --target local
# Или OAuth2:
dotnet run --project tools/BikeHub.Tools -- mail gmail --target local --client-json '/путь/к/client.json' --sender sender@gmail.com
```

Для проверки входа через почтовый сервис запустите основной API в другом терминале:

```bash
Authentication__EmailProvider=MailService \
MailService__ApiKey=bikehub-local-mail-api-key-for-development \
dotnet run --project src/BikeHub.Api --launch-profile http
```

`MailService:BaseUrl` по умолчанию `http://127.0.0.1:5181`. Локальный ключ выше предназначен только для разработки. Без этих переопределений Development основного API сохраняет прежний сценарий с тестовым письмом в форме и не требует Kafka или почтового сервиса.

Остановка зависимостей без удаления данных:

```bash
docker compose down
```

## HTTP API

Все маршруты `/api/*` требуют `X-Api-Key`. В production задайте случайный `Mail:ApiKey` длиной не менее 32 символов; тот же ключ должен быть в `MailService:ApiKey` основного API. Ключи, OAuth-токены и пароли не передаются в URL.

| Метод | Путь | Результат |
| --- | --- | --- |
| POST | `/api/emails` | 202: письмо записано в очередь; повтор терминального письма возвращает 200 с его статусом |
| GET | `/api/emails/{id}` | Текущий статус без адреса и текста; 404 для неизвестного ID |
| GET | `/health/live` | Процесс работает; без API-ключа |
| GET | `/health/ready` | PostgreSQL доступен, все миграции применены; без API-ключа |

Пример для Development:

```bash
curl -X POST http://127.0.0.1:5181/api/emails \
  -H 'X-Api-Key: bikehub-local-mail-api-key-for-development' \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: 649a3a9d-bca5-43f9-8649-a24052ca8627' \
  -d '{"messageId":"649a3a9d-bca5-43f9-8649-a24052ca8627","destination":"rider@example.test","subject":"BikeHub","textBody":"Тестовое письмо"}'
```

`messageId` — обязательный UUID и общий ключ идемпотентности для обоих входов. Не создавайте новый ID при повторе запроса. Необязательный заголовок `Idempotency-Key` должен совпадать с `messageId`, иначе будет 400. Нулевой UUID, неверный адрес, слишком длинные поля и переводы строк в заголовке письма отклоняются с 400. Тело ограничено 256 KB, текст — 100 000 символов.

`expiresAt` — необязательный UTC timestamp. Для кодов входа основной API всегда передаёт срок действия кода. Просроченные письма не отправляются; без `expiresAt` прикладного срока нет.

## Kafka

Топик: `bikehub.mail.send.v1`. Значение — JSON с теми же полями, что в HTTP API; Kafka key не является ключом идемпотентности. После HTTP-примера можно отправить тот же JSON через Kafka — второго письма не будет:

```bash
docker compose exec -T kafka \
  /opt/kafka/bin/kafka-console-producer.sh --bootstrap-server kafka:9092 --topic bikehub.mail.send.v1 <<'JSON'
{"messageId":"649a3a9d-bca5-43f9-8649-a24052ca8627","destination":"rider@example.test","subject":"BikeHub","textBody":"Тестовое письмо"}
JSON
```

Настройки: `Kafka:Enabled`, `BootstrapServers`, `Topic`, `GroupId`, `SecurityProtocol`, `SaslMechanism`, `SaslUsername`, `SaslPassword`, `SslCaLocation`. SASL/TLS поддерживаются клиентом; параметры передаются окружением или защищённой конфигурацией. Производители должны иметь право писать в топик, consumer — читать топик и свою группу. Локальный брокер использует PLAINTEXT, публикует порт только на loopback и хранит сообщения один час. В production настройте подходящие ACL, TLS и retention для персональных данных.

Consumer отключает автоматический commit и offset store. Offset фиксируется **после записи в PostgreSQL**. При недоступной БД текущая запись повторяется без продвижения offset. После crash/rebalance повторное чтение безопасно благодаря тому же `messageId`. Семантика ручного commit описана в [официальной документации Confluent](https://docs.confluent.io/kafka-clients/dotnet/current/overview.html).

Невалидные JSON/контракты и конфликты содержимого фиксируются в `mail.kafka_rejections` по `(topic, partition, offset)`, затем offset подтверждается, чтобы повреждённая запись не блокировала топик. Журнал хранит только координаты и фиксированную причину, без сырого payload, адреса или кода. Ошибки БД не считаются повреждёнными сообщениями. Readiness отражает доступность БД и применение миграций; состояние Kafka проверяйте отдельно по журналу consumer и lag группы.

## Идемпотентность и доставка

HTTP и Kafka используют `mail.messages` с первичным ключом `messageId`. INSERT с `ON CONFLICT DO NOTHING` атомарно выбирает единственную запись. SHA-256 нормализованного контракта проверяет содержимое: адрес приводится к нижнему регистру, время — к UTC; остальные поля должны совпадать. Повтор с другим содержимым даёт HTTP 409 либо запись `idempotency_conflict` для Kafka. Это работает между репликами и после перезапуска.

Worker забирает запись под `FOR UPDATE SKIP LOCKED` и **до вызова провайдера** сохраняет состояние `sending`. Возможные статусы:

| Статус | Значение |
| --- | --- |
| `queued` | Ожидает обработки или безопасного повтора |
| `sending` | Начата попытка отправки |
| `sent` | Провайдер принял письмо; доставка в Inbox этим не гарантируется |
| `failed` | Определённый отказ либо исчерпаны попытки |
| `expired` | Истёк срок действия до отправки |
| `unknown` | Результат нельзя установить без риска повторной отправки |

Ошибки подключения до отправки и определённые временные отказы повторяются с экспоненциальной задержкой: по умолчанию максимум 5 попыток, первая задержка 30 секунд (`Mail:MaxAttempts`, `RetrySeconds`). Ошибки учётных данных не повторяются автоматически. Срок кода ограничивает повторы. Терминальные статусы не переотправляются при повторе HTTP/Kafka.

SMTP/Gmail не участвуют в транзакции PostgreSQL. Если провайдер мог принять письмо, но ответ потерян, либо процесс упал после фиксации `sending`, состояние становится `unknown` (зависшая попытка — через 120 секунд). Автоматического повтора нет. Это сознательный выбор в пользу отсутствия дублей: в этом окне письмо могло и не отправиться. Статус требует проверки у провайдера; API принудительной переотправки не предусмотрен. Стабильный MIME Message-ID помогает искать письмо, но не является гарантией дедупликации почтовым сервером.

После терминального состояния адрес, тема и текст очищаются из очереди. ID, fingerprint, статус и счётчики сохраняются без автоматического удаления: удаление этой записи отменило бы защиту от позднего повтора. Учитывайте отдельно срок хранения Kafka, WAL и резервных копий PostgreSQL.

## Развёртывание на NAS

Используется прежний `ansible-playbook ... infra/ansible/playbooks/server.yml`. Он собирает три образа (API, Web и Mail), применяет миграции почтового сервиса и ждёт его готовности **до** обновления основного API. Новую версию впервые выкладывайте со сборкой; `bikehub_rebuild_image=false` предназначен только для уже установленных версий всех трёх сервисов.

Существующий Secret `bikehub-auth` сохраняется: SMTP/Gmail-ключи подключаются только к `bikehub-mail`, OTP-ключ — только к основному API. Случайный ключ внутреннего HTTP API создаётся один раз непосредственно на VM в Secret `bikehub-mail`; он не выводится и не копируется на контроллер. Новые OAuth-токены для переноса не нужны. Повторная выкладка сохраняет ключ. При его ротации rollout обоих сервисов зависит от revision Secret.

Почтовый сервис имеет собственные `MailDbContext`, схему `mail` и EF journal. По умолчанию NAS передаёт существующее подключение из `bikehub-database` через `ConnectionStrings__Mail` без чтения значения на контроллер. Для отдельной БД задайте `bikehub_mail_database_secret_name` на Secret с ключом `connection-string` и заранее создайте БД. Начальная миграция использует raw SQL с `IF NOT EXISTS`; автоматический Down запрещён.

По умолчанию Ansible создаёт внутренний Kafka KRaft с одним брокером, PVC 2 GiB, топиком на три партиции и retention один час. Это конфигурация для домашнего single-node NAS без отказоустойчивости; брокер не публикуется в LAN, NetworkPolicy ограничивает входящие соединения namespace BikeHub. Для существующего брокера задайте `bikehub_mail_kafka_managed=false`, `bikehub_mail_kafka_servers`; топик создайте отдельно. Для HTTP без consumer можно временно задать `bikehub_mail_kafka_enabled=false`.

Mail API остаётся ClusterIP без публичного nginx-маршрута. Логи:

```bash
ssh -i ~/.ssh/truenas_admin ansible@192.168.0.49 \
  'sudo -n k3s kubectl logs -n bikehub deployment/bikehub-mail --tail=200 --follow'
```

OpenTelemetry использует service name `bikehub-mail`, метрику `bikehub.mail.deliveries`, HTTP/worker traces и журнал с ID, статусом и безопасной причиной. Адреса, текст письма и OAuth-токены не добавляются в телеметрию.

## Проверки

```bash
dotnet test BikeHub.sln --disable-build-servers
```

Testcontainers поднимает одноразовые PostgreSQL и Kafka автоматически и удаляет их после тестов. Нужен Docker; отдельный Compose или переменные с подключениями для тестов не требуются. Проверяются гонки отправителей/worker, общий HTTP/Kafka ключ, конфликт содержимого, poison records, отсутствие commit при отказе БД, просроченные письма, backoff, `unknown` после сбоя и удаление содержимого после отправки. Реальные письма тесты не отправляют.
