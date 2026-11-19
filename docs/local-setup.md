# Локальный запуск: API + SQL Server

**Статус:** инструкции подготовлены, но сборка и реальные SQL-интеграционные проверки в текущей среде не проводились.

## Установить
- Visual Studio 2022 с ASP.NET и .NET 8 SDK.
- SQL Server Express/Developer и SQL Server Management Studio (SSMS).
- Android Studio потребуется на следующем этапе.

## Шаг 1: база
В SSMS подключитесь к своему SQL Server и выполните по очереди:
1. `database/schema.sql`
2. `database/seed.sql`

Должны появиться зал с 12 игровыми местами, четыре тарифных плана и демонстрационный пользователь `demo_241810`. Это синтетическая запись без реального пароля; seed-пользователь не имеет пригодного пароля и не используется для входа. Для проверки авторизации создайте VISITOR через `/api/auth/register`. Узнать ID пользователя:

```sql
USE GamingClub;
SELECT UserId, Login FROM dbo.Users WHERE Login=N'demo_241810';
SELECT SeatId, SeatNumber FROM dbo.Seats ORDER BY SeatNumber;
SELECT TariffId, Name FROM dbo.Tariffs ORDER BY TariffId;
```

## Миграции существующей базы

Если база была создана до появления отмены бронирований, выберите `GamingClub` в SSMS и запустите `database/migrations/001_void_pending_payments.sql`. Для только что созданной базы с актуальным `schema.sql` миграция не нужна. Миграция добавляет статус `VOID` для неоплаченного счёта.

## Шаг 2: строка подключения
Установите **локально** переменную окружения `ConnectionStrings__GamingClub`, например в PowerShell:

```powershell
$env:ConnectionStrings__GamingClub = "Server=localhost\\SQLEXPRESS;Database=GamingClub;Trusted_Connection=True;TrustServerCertificate=True"
```

При именованном экземпляре SQL Server замените адрес сервера на свой. Не записывайте пароль в Git.

## Шаг 3: API

```powershell
dotnet restore src/GamingClub.API/GamingClub.API.csproj
dotnet run --project src/GamingClub.API --urls http://localhost:5080
```

Открыть `http://localhost:5080/swagger` (если `ASPNETCORE_ENVIRONMENT=Development`), затем:
- `GET /health` — статус сервера
- `GET /api/seats` — зал и координаты мест
- `GET /api/tariffs` — тарифы
- `GET /api/dev/seats/1/bookings` — брони места
- `POST /api/dev/bookings` — **только локальная разработка**, не публиковать в интернет
- `GET /api/dev/reports/occupancy` — процент загрузки за интервал UTC
- `GET /api/dev/reports/occupancy.csv` — экспорт CSV; сценарии проверки в `docs/report-verification.md`
- `POST /api/dev/sessions/{bookingId}/start` — запуск сессии по действующей брони
- `POST /api/dev/sessions/{bookingId}/finish` — ручное завершение сессии и перевод брони в `COMPLETED`
- `GET /api/dev/sessions/{bookingId}` — оставшееся время; проверка в `docs/session-verification.md`
- `POST /api/dev/sessions/{bookingId}/extend` — продление с проверкой конфликта и доплатой; проверка в `docs/extension-verification.md`

Пример JSON для тестового бронирования (используйте реальные ID из SELECT и будущий UTC-интервал):

```json
{
  "userId": 1,
  "seatId": 1,
  "tariffId": 1,
  "startUtc": "2026-11-20T14:00:00Z",
  "endUtc": "2026-11-20T15:00:00Z"
}
```

Повторная бронь того же места с пересечением должна возвращать HTTP 409.
Смежные интервалы, например 15:00–16:00, допустимы.

## Шаг 4: модульные тесты

```powershell
dotnet test tests/GamingClub.Tests/GamingClub.Tests.csproj --collect:"XPlat Code Coverage"
```

Тесты тарификации, отчёта, продления и авторизации выполняются в CI. Зафиксированы 85 PASS .NET-тестов, включая SQL/HTTP сценарии, и покрытие 85,05% строк / 72,25% ветвей. Локальный запуск требует .NET SDK и SQL Server.

## Авторизация и роли

Для JWT необходимо также локально установить `GAMINGCLUB_JWT_SECRET` (минимум 32 байта); команды, API и матрица прав описаны в `docs/auth-verification.md`. Для Android использовать регистрацию через `/api/auth/register` или вход через `/api/auth/login`. Защищённые операции должны получать заголовок `Authorization: Bearer <token>`.

## Отмена бронирования

После регистрации JWT-пользователя доступен `POST /api/bookings/{bookingId}/cancel`, для администратора `POST /api/admin/bookings/{bookingId}/cancel`. Проверка и ограничения описаны в `docs/cancellation-verification.md`.

## Предупреждение
Это учебный прототип. Эндпоинт бронирования без аутентификации существует только в Development; используйте исключительно локальный адрес `localhost` и не открывайте его в публичную сеть. Следующий этап — безопасная авторизация, роли и ограничение доступа к операциям.
