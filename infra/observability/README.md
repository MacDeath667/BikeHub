# Grafana и OpenTelemetry

Grafana остаётся существующим приложением TrueNAS: [https://truenas.local:30037/](https://truenas.local:30037/).
Дашборд: [BikeHub · API](https://truenas.local:30037/d/bikehub-api).

API отправляет метрики, структурированные логи и трассировки по OTLP/HTTP в Collector на `bikehub01`.
Collector передаёт логи в Loki, трассировки в Tempo и отдаёт метрики Prometheus.
В логах сохраняются trace ID и span ID; в Grafana можно открыть трассировку из строки лога и вернуться к её логам.

## Локальная разработка

После первоначального развёртывания хранилищ запускайте API обычной командой:

```sh
dotnet run --project src/BikeHub.Api --launch-profile http
```

В `Development` экспорт включён, endpoint — `http://bikehub.local:30418`.
Это адрес Collector, а не адрес интерфейса Grafana. Локальный Docker для телеметрии не требуется.
Выберите `Development` в фильтре «Окружение» на дашборде; серверная версия имеет метку `Production`.

Переопределить адрес или выключить экспорт:

```sh
OTEL_EXPORTER_OTLP_ENDPOINT=http://192.168.0.49:30418 \
  dotnet run --project src/BikeHub.Api --launch-profile http

Observability__Enabled=false \
  dotnet run --project src/BikeHub.Api --launch-profile http
```

Вне `Development` экспорт по умолчанию выключен. Ansible включает его для Deployment и задаёт внутренний адрес Collector.
`--migrate-only` не запускает телеметрию. Запросы `/api/health` исключены из трассировок и собственного HTTP-лога.
В текст HTTP-лога входят метод, путь без query string, статус и время; тела запросов, cookies и Authorization не записываются.
Метрики HTTP используют шаблон маршрута, а не идентификаторы конкретных велосипедов и записей.

## Развёртывание

Сборка API, фронта и почтового сервиса выполняется обычным [Ansible deploy](../README.md#обновление-приложения-в-существующей-vm). Укажите `-e bikehub_auth_enabled=true`, чтобы сохранить включённый вход. Для применения конфигурации с текущими образами добавьте `-e bikehub_rebuild_image=false`.

Логи API:

```sh
ssh -i ~/.ssh/truenas_admin ansible@192.168.0.49 \
  'sudo -n k3s kubectl logs -n bikehub deployment/bikehub-api --container=api --tail=200 --follow'
```

`Ctrl+C` завершает просмотр логов. Те же данные доступны на дашборде в панели «Логи API»; для VM выберите окружение `Production`.

Для отдельного обновления наблюдаемости без выкладки приложения запустите из корня репозитория:

```sh
export BIKEHUB_VM_IP=192.168.0.49
ansible-playbook -i "$BIKEHUB_VM_IP," -u ansible \
  --private-key ~/.ssh/truenas_admin \
  infra/ansible/playbooks/observability.yml \
  -e @infra/ansible/inventory/group_vars/bikehub/main.yml \
  -e bikehub_target_group=all
```

Playbook разворачивает сервисы в отдельном namespace `bikehub-observability`, затем обновляет provisioning существующей Grafana через SSH и API TrueNAS. Используется `truenas_admin@truenas.local`; права должны разрешать управление приложением Grafana и запись трёх публичных файлов конфигурации. Шаблоны datasource/provider формируются в Ansible. Единственный оставшийся Python-файл `truenas-provision.py` — адаптер файловой загрузки и app API TrueNAS 25.10; он запускается Ansible непосредственно на NAS с публичной конфигурацией через stdin. Пароль Grafana и постоянный API-токен не требуются. Для файловой загрузки используется одноразовый токен TrueNAS со сроком 30 секунд; он остаётся внутри NAS и не сохраняется.

Файлы Grafana находятся в её существующем постоянном томе:

```text
/var/lib/grafana/data/bikehub-provisioning/
  datasources/bikehub.yml
  dashboards/bikehub.yml
  dashboards-json/bikehub-api.json
```

`GF_PATHS_PROVISIONING` указывает на этот каталог. Если Grafana уже настроена на другой каталог provisioning, настройка остановится: конфигурации нужно объединить явно. Playbook меняет только источники `bikehub-prometheus`, `bikehub-loki`, `bikehub-tempo`, папку BikeHub и дашборд `bikehub-api`. Повторный запуск с теми же файлами не перезапускает Grafana. Дашборд и источники редактируются в Git.

Для Grafana выбран существующий сертификат TrueNAS с ID `2`, покрывающий `truenas.local`. Прежний `truenas_default` с ID `1` выпущен для `localhost` и не подходит этому адресу. Параметр `bikehub_grafana_certificate_id` можно заменить на другой подходящий сертификат. API TrueNAS и Grafana проверяются по существующему публичному CA `~/.config/truenas/pki/bikehub-ca.crt`; закрытый ключ CA не нужен. Сертификат самого сайта BikeHub — отдельная пара `bikehub_tls`.

Обычный `server.yml` также поддерживает сервисы сбора данных и включает экспорт API. Для обновления образа API после подключения используйте обычный деплой приложения, описанный в [инструкции TrueNAS](../README.md). Для полностью отключённой интеграции при деплое задайте `-e bikehub_observability_enabled=false`; уже накопленные данные и PVC сохранятся.

## Порты и хранение

| Сервис | Адрес в домашней сети | Хранение |
| --- | --- | --- |
| OTLP HTTP | `http://bikehub.local:30418` | Collector отправляет данные в хранилища |
| Prometheus | `http://192.168.0.49:30900` | 7 дней, TSDB до 4 GB, PVC 5 Gi |
| Loki | `http://192.168.0.49:31000` | 7 дней, PVC 5 Gi |
| Tempo | `http://192.168.0.49:32000` | 48 часов, PVC 2 Gi |

NodePort предназначены для домашней сети; аутентификация между этими сервисами не включена. Данные хранятся в `local-path` PVC на существующем диске VM и переживают перезапуски pod. Размер запроса PVC у local-path не является файловой квотой. При смене адреса VM повторите `observability.yml`, чтобы обновить URL источников Grafana.

Образы закреплены в `group_vars/bikehub/main.yml`. Tempo 3.1 требует инструкции x86-64-v2, поэтому для этой VM в Terraform установлен `cpu_mode = "HOST-PASSTHROUGH"`; generic CUSTOM их скрывал. Включение этого режима требует штатной остановки и запуска VM. Миграция на другой физический сервер потребует совместимого CPU.

## Проверка

С запущенным локальным API:

```sh
dotnet run --project tools/BikeHub.Tools -- observability \
  --api-url http://127.0.0.1:5179 --vm-address 192.168.0.49
```

Проверка развёрнутого API:

```sh
dotnet run --project tools/BikeHub.Tools -- observability \
  --api-url https://bikehub.local --vm-address 192.168.0.49 --environment Production \
  --ca-certificate infra/ansible/.local-pki/bikehub_tls.pub
```

Сквозная проверка делает один GET `/api/feed` с новым W3C trace ID, находит эту трассировку в Tempo, HTTP-лог с тем же trace ID в Loki и HTTP-счётчик того же экземпляра API в Prometheus. Данные могут появляться до 30 секунд из-за периодов экспорта и scrape. Проверка не создаёт велосипеды, записи или комментарии.

Схема передачи соответствует [экспорту OTLP в .NET](https://opentelemetry.io/docs/languages/dotnet/exporters/) и [приёму OTLP-логов в Loki](https://grafana.com/docs/loki/latest/send-data/otel/).
