# BikeHub на TrueNAS

Terraform управляет существующей Debian VM на TrueNAS; Ansible собирает API и фронт, импортирует образ в K3s и настраивает nginx. Данные внешнего PostgreSQL и его сервис Ansible не пересоздаёт.

## Текущий сервер

- TrueNAS 25.10.6: `truenas.local`, `192.168.0.216`.
- VM: `bikehub01`, TrueNAS ID `7`, Debian 13, диск `Applications/bikehub01-os`.
- Мост `br1` уже настроен: член `enp4s0`, management IP NAS находится на мосту.
- MAC существующей VM: `00:a0:98:15:33:1f`. Сохраните его для стабильной DHCP-идентичности.
- Адрес VM на момент настройки: `192.168.0.49`. Имя сайта — `bikehub.local`; собственное имя VM `bikehub01.local` также сохраняется.
- Часы VM в TrueNAS должны быть **UTC** (`time = "UTC"`). Debian использует UTC для аппаратных часов.

Перед подключением можно проверить адрес через `python3 -c 'import socket; print(socket.gethostbyname("bikehub01.local"))'`. Для постоянного адреса закрепите текущий MAC в DHCP роутера.

## Обновление приложения в существующей VM

Запускайте из корня репозитория. Требуются Python 3, Ansible и ключ `~/.ssh/truenas_admin`. В Debian должны быть доступны APT и реестры контейнеров для первой загрузки базовых образов.

```sh
export BIKEHUB_VM_IP=192.168.0.49
ansible-playbook -i infra/ansible/inventory/hosts.yml \
  infra/ansible/playbooks/server.yml --ask-vault-pass
```

`vault.yml` в inventory зашифрован и игнорируется Git. Если в нём передан `bikehub_database_connection_string`, playbook обновит Kubernetes Secret. Если строка подключения не передана, используется **существующий** `bikehub-database`; содержимое Secret на контроллер не копируется. На первом развёртывании Secret нужно предоставить.

Для повторного деплоя с существующим Secret без открытия локального Vault используйте host-list inventory (у него нет соседнего `group_vars`):

```sh
ansible-playbook -i "$BIKEHUB_VM_IP," -u ansible \
  --private-key ~/.ssh/truenas_admin \
  infra/ansible/playbooks/server.yml \
  -e @infra/ansible/inventory/group_vars/bikehub/main.yml \
  -e bikehub_target_group=all
```

Сборка упаковывает только `.dockerignore`, `src` и `Web`, без `bin`, `obj`, `node_modules`, `dist`, `.env*`, Terraform state и Vault. Хеш исходников входит в тег образа; новая сборка меняет Deployment и запускает rollout. Временный архив удаляется. Источники собираются в `/opt/bikehub/build`, Kubernetes manifests — в `/opt/bikehub/k8s`.

Повторное применение настроек без пересборки:

```sh
ansible-playbook -i "$BIKEHUB_VM_IP," -u ansible \
  --private-key ~/.ssh/truenas_admin \
  infra/ansible/playbooks/server.yml \
  -e @infra/ansible/inventory/group_vars/bikehub/main.yml \
  -e bikehub_target_group=all -e bikehub_rebuild_image=false
```

Будет использован образ из действующего Deployment. Можно явно передать `-e bikehub_image=docker.io/library/bikehub-api:build-<hash>`, если он уже импортирован в K3s.

## Настройки Ansible

Основные параметры: `ansible/inventory/group_vars/bikehub/main.yml`.

- `bikehub_replica_count: 1`: у текущих моков данные хранятся в памяти процесса. Несколько реплик допустимы после подключения общих постоянных сервисов; одни миграции PostgreSQL этого не меняют.
- `bikehub_database_enabled: true`: использовать внешний PostgreSQL и запускать миграции перед обновлением приложения. Для демонстрации только с моками можно передать `-e bikehub_database_enabled=false`.
- `bikehub_database_bootstrap_enabled: true`: Job создаёт БД, если она отсутствует; учётная запись должна иметь соответствующее право. Для заранее созданной БД и ограниченного пользователя задайте `false`.
- `bikehub_truenas_ip`: по умолчанию `192.168.0.216`; переопределяется `TRUENAS_IP` или extra vars.
- `bikehub_truenas_hostname: truenas.local`: в CoreDNS настраивается отдельная зона. Pod не полагаются на mDNS хоста.
- `bikehub_migration_retention: 86400`: завершённые Job сохраняются сутки для диагностики.

K3s использует ConfigMap **`coredns-custom`** в `kube-system`. Playbook управляет только ключом `truenas.server` через server-side apply и сохраняет остальные зоны. После реального изменения DNS выполняется rollout CoreDNS. Конфликт владельцев поля не перезаписывается принудительно.

Проверки после выкладки:

```sh
curl --cacert infra/ansible/.local-pki/bikehub_tls.pub https://bikehub.local/api/health
ssh -i ~/.ssh/truenas_admin "ansible@$BIKEHUB_VM_IP" \
  'sudo k3s kubectl get pods,deploy,jobs -n bikehub'
```

Сам playbook проверяет готовность pod, HTTPS health API с проверкой цепочки сертификата и наличие фронтенда через nginx. Сайт доступен на **https://bikehub.local/**. HTTP перенаправляется на этот адрес с кодом 308.

## Короткое имя сайта

`bikehub.local` публикуется Avahi как дополнительное имя текущего LAN-адреса VM. Имя VM в TrueNAS и её системный hostname остаются `bikehub01`; `bikehub01.local` продолжает работать. Служба `bikehub-mdns.service` запускает `avahi-publish-address --no-reverse --no-fail` и автоматически стартует после загрузки. Отдельная обратная запись не создаётся, чтобы не конфликтовать с основным именем VM.

Добавить или обновить только имя, без сборки и миграций:

```sh
ansible-playbook -i "$BIKEHUB_VM_IP," -u ansible \
  --private-key ~/.ssh/truenas_admin \
  infra/ansible/playbooks/hostname.yml \
  -e @infra/ansible/inventory/group_vars/bikehub/main.yml \
  -e bikehub_target_group=all
```

Эта задача также входит в `server.yml` и `https.yml`. После изменения DHCP-адреса повторите playbook с новым `BIKEHUB_VM_IP`; предпочтительно закрепить адрес по MAC в DHCP роутера. При включении HTTPS сертификат и HTTP redirect используют короткое имя `bikehub.local`.

## HTTPS в домашней сети

TLS завершается в nginx на порту 443. Ansible создаёт отдельную самоподписанную пару для `bikehub.local` и текущего IPv4 VM:

- `/etc/nginx/bikehub-tls/bikehub_tls` — закрытый ключ RSA 2048 без пароля, владелец `root`, права `0600`. Он остаётся на VM.
- `/etc/nginx/bikehub-tls/bikehub_tls.pub` — публичный сертификат X.509 в PEM. Расширение `.pub` здесь обозначает сертификат TLS.
- `infra/ansible/.local-pki/bikehub_tls.pub` — публичная копия на контроллере для проверки и настройки доверия на устройствах. Каталог игнорируется Git.

Сертификат имеет назначение `serverAuth` и `CA:FALSE`: он удостоверяет сайт, а не выпускает другие сертификаты. Старый ключ CA для выпуска не требуется. На домашних устройствах нужно явно доверить новый публичный сертификат; до этого браузер покажет предупреждение.

Зависимости: коллекция `community.crypto` на контроллере и `python3-cryptography` на VM (устанавливается playbook). Коллекцию можно установить командой:

```sh
ansible-galaxy collection install -r infra/ansible/requirements.yml
```

Параметры в `group_vars/bikehub/main.yml`:

- `bikehub_tls_enabled: true` — включить HTTPS и перенаправление HTTP.
- `bikehub_hostname: bikehub.local` — имя сайта, публикуемое Avahi в домашней сети.
- `bikehub_tls_server_name` — по умолчанию использует `bikehub_hostname`.
- `bikehub_tls_name: bikehub_tls` — имя пары ключ/сертификат.
- `bikehub_tls_directory` — каталог ключа и сертификата на VM.

Настроить или обновить только nginx/TLS без сборки приложения и миграций:

```sh
ansible-playbook -i "$BIKEHUB_VM_IP," -u ansible \
  --private-key ~/.ssh/truenas_admin \
  infra/ansible/playbooks/https.yml \
  -e @infra/ansible/inventory/group_vars/bikehub/main.yml \
  -e bikehub_target_group=all
```

Обычный `server.yml` также включает эти задачи. Сертификат действует 365 дней; при запуске Ansible он перевыпускается, если остаётся менее 30 дней, меняется имя/IP или сертификат ещё не действителен. Повторный запуск с действующим сертификатом сохраняет существующую пару. Если долго нет обычных выкладок, запускайте `https.yml` для продления до истечения срока. После перевыпуска самоподписанного сертификата потребуется обновить доверие на устройствах.

Проверить HTTPS с явным доверием только этому сертификату:

```sh
curl --cacert infra/ansible/.local-pki/bikehub_tls.pub https://bikehub.local/api/health
```

Чтобы браузеры macOS доверяли сертификату, пользователь может добавить его в свою связку ключей с ограничением на SSL и имя сайта:

```sh
security add-trusted-cert -r trustRoot -p ssl -s bikehub.local \
  infra/ansible/.local-pki/bikehub_tls.pub
```

macOS может запросить подтверждение. Playbook не меняет системное доверие на Mac или VM. На других устройствах импортируйте только публичный `bikehub_tls.pub`, закрытый ключ для этого не нужен.

Проверки Ansible используют публичный сертификат как явный доверенный и обязательно проверяют подпись, срок, назначение и имя/IP. В nginx разрешены TLS 1.2 и 1.3. Перед заменой конфигурации Ansible проверяет новый фрагмент через `nginx -t`; после изменения сертификата nginx перечитывает его без остановки приложения. Проверочный процесс использует отдельные временные каталоги, чтобы не менять права рабочих буферов nginx. Задачи nginx также восстанавливают владельца `www-data` для каталогов `/var/lib/nginx/{body,proxy,fastcgi,scgi,uwsgi}`: неверные права могут обрывать большие ответы JavaScript и изображений при успешном ответе главной страницы.

## Восстановление K3s после неверного времени

При загрузке с виртуальными часами LOCAL VM сначала получила время на три часа впереди UTC. K3s выпустил сертификаты, затем NTP перевёл часы назад: компоненты получили `certificate ... not yet valid` и `Unauthorized`.

Исправление: режим UTC в TrueNAS, `systemd-timesyncd` в гостевой ОС и systemd dependency K3s на `systemd-time-wait-sync.service`. Если сертификаты уже выпущены в будущем, выполните один деплой с дополнительным параметром:

```sh
# Добавьте к обычной команде ansible-playbook только для восстановления:
-e bikehub_rotate_k3s_certificates=true
```

Playbook сначала дождётся синхронизации времени, затем остановит K3s, обновит leaf-сертификаты и запустит K3s обратно. Системные pod также перезапускаются, чтобы заменить токены, выпущенные с неверным временем. CA кластера сохраняется. Обычно этот параметр выключен. Внешние копии admin kubeconfig после ротации потребуется обновить.

## Terraform: существующая VM

Локальный `terraform/terraform.tfvars` и state не коммитятся. В нём для существующей VM должны сохраняться фактические значения, в частности:

```hcl
vm_running = true
vm_nic_mac = "00:a0:98:15:33:1f"
```

Не заменяйте MAC значением из примера при работе с уже созданной VM. `main.tf` задаёт UTC и `prevent_destroy` для OS zvol. Не удаляйте state и не импортируйте образ поверх существующего системного диска.

```sh
terraform -chdir=infra/terraform init -lockfile=readonly
terraform -chdir=infra/terraform validate
# В своём терминале установите TRUENAS_API_KEY; значение не коммитится.
terraform -chdir=infra/terraform plan
```

Просмотрите план перед применением. Полный plan/apply требует API-ключ TrueNAS; одного SSH-доступа недостаточно для Terraform provider. Для изменений уже созданной VM, которым не нужны image/seed provisioners, применяйте обычный `terraform -chdir=infra/terraform apply`.

## Создание VM с нуля — отдельный сценарий

Копируйте `terraform/terraform.tfvars.example` в `terraform/terraform.tfvars` только для новой VM и отдельного state. Настройте endpoint, CA, пул, уникальные имя и MAC, SSH target и путь seed ISO. Сначала `vm_running=false`: создаются zvol, VM и устройства, скачивается закреплённый Debian cloud image и готовится NoCloud seed. После успешной подготовки задайте `vm_running=true` во втором apply.

Образ закреплён на Debian 13 build `20261005-2622`; import script проверяет SHA-512 и отказывается записывать в zvol с непустым первым блоком. Для создания ISO нужны `hdiutil` на macOS либо `genisoimage` на Linux. Provisioners выполняются по SSH; wrapper запрашивает sudo-пароль в терминале и передаёт его через stdin, без записи в Terraform state:

```sh
export TRUENAS_API_KEY='...'
infra/terraform/terraform-apply.sh -var='vm_running=false'
infra/terraform/terraform-apply.sh -var='vm_running=true'
```

Wrapper выбирает каталог Terraform самостоятельно. Cloud-init создаёт пользователя `ansible` с ключом, passwordless sudo и отключённым password/root SSH. Нормальный деплой использует ключ; legacy playbook `bootstrap-password.yml` для него не требуется.

## Локальные проверки

```sh
terraform -chdir=infra/terraform fmt -check
terraform -chdir=infra/terraform validate
ansible-playbook -i infra/ansible/inventory/hosts.yml \
  infra/ansible/playbooks/server.yml --syntax-check
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s infra/tests -v
bash -n infra/terraform/terraform-apply.sh
```

Документация: [CoreDNS custom imports в K3s](https://docs.k3s.io/advanced#coredns-custom-configuration-imports), [ротация сертификатов K3s](https://docs.k3s.io/cli/certificate#rotating-clientserver-certificates), [ресурс TrueNAS VM](https://registry.terraform.io/providers/truenas/truenas/latest/docs/resources/vm).
