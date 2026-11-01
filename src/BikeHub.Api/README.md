# HTTP API v1

Запуск: `dotnet run --project src/BikeHub.Api --launch-profile http` из корня. Development не требует PostgreSQL. Swagger: http://localhost:5179/swagger.

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/api/health` | Состояние API и источник данных |
| GET | `/api/feed` | Лента: `tab=all/following/popular/saved`, `category`, `q` |
| GET | `/api/feed/overview` | Счётчики, теги и велосипед недели |
| GET, POST | `/api/bikes` | Каталог (`q`, `category`, `ownerId`) и добавление велосипеда |
| GET | `/api/bikes/{id}` | Карточка велосипеда |
| GET | `/api/bikes/{id}/history` | Бортжурнал |
| GET | `/api/users/{id}/bikes` | Гараж владельца; прежний `/api/user/{id}/bikes` также поддерживается |
| GET | `/api/users/me` | Текущий подтверждённый пользователь; 401 для гостя |
| GET | `/api/users/{id}` | Профиль и счётчики |
| PUT, DELETE | `/api/users/{id}/follow` | Подписаться / отписаться |
| POST | `/api/posts` | Запись в бортжурнал своего велосипеда |
| GET | `/api/posts/{id}` | Полная запись |
| PUT, DELETE | `/api/posts/{id}/like` | Поставить / убрать лайк |
| PUT, DELETE | `/api/posts/{id}/bookmark` | Сохранить / удалить закладку |
| GET, POST | `/api/posts/{id}/comments` | Комментарии (сначала новые) и создание комментария |
| GET | `/api/communities` | Сообщества (`q`) |
| PUT, DELETE | `/api/communities/{id}/membership` | Вступить / выйти |

Списки ленты, велосипедов, бортжурнала и комментариев принимают `page` (от 1) и `pageSize` (1–50, по умолчанию 10). Ответ: `{ items, total, pageNumber, pageSize, hasMore }`. Изменения возвращают актуальный DTO; повторные PUT/DELETE не меняют счётчики повторно. Создание возвращает 201 и `Location`, некорректные данные — 400 с `ValidationProblemDetails`, неизвестные ресурсы — 404.

Категории велосипедов: `road`, `mtb`, `gravel`, `city`. Типы записей: `ride`, `maintenance`, `upgrade`, `story`.

В C# категории велосипедов и сообществ представлены enum `BikeCategory`; в JSON они передаются строками. Поле категории обязательно при создании велосипеда, числовые JSON-значения не принимаются. Неизвестная категория в фильтрах ленты и каталога возвращает 400.

Текущий пользователь определяется по серверной сессии после подтверждения почты. `ownerId` и автор записи назначаются сервисом. Запись можно создать только для собственного велосипеда. Данные хранятся в памяти процесса и сбрасываются при перезапуске. Контент пока остаётся в моках; аккаунты и сессии поддерживают PostgreSQL. Все изменяющие запросы требуют авторизации и заголовка `X-CSRF-TOKEN`. Закладки, лента подписок и `/users/me` требуют входа. Контракты `/api/auth`, настройка Gmail и ограничения описаны в [инструкции авторизации](../../docs/authentication.md).
