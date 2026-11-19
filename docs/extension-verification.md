# Продление игровой сессии: ручная проверка

**Статус:** исходный код и SQL/HTTP-проверки добавлены. Полный пользовательский UI-прогон ещё требуется выполнить локально.

## Предусловия

1. Выполнить `database/schema.sql` и `database/seed.sql` в SQL Server.
2. Запустить API в режиме Development только локально: `http://localhost:5080`.
3. В Swagger создать бронь тарифом `HOURLY`, начиная с нескольких минут в прошлом и заканчивая через 20–30 минут в будущем.
4. Запустить её: `POST /api/dev/sessions/{bookingId}/start`.

## Запрос продления

```http
POST /api/dev/sessions/{bookingId}/extend
Content-Type: application/json

{"newEndUtc":"2026-11-20T16:00:00Z"}
```

Вместо даты из примера поставить время ПОЗЖЕ фактического конца брони, строго целую минуту UTC.

## Ожидаемый результат

- Если место свободно, HTTP 200; ответ содержит `newEndUtc` и `extraChargeRub`.
- `dbo.Bookings.EndUtc` и `dbo.Sessions.EndsUtc` должны стать одинаковыми.
- При положительной доплате появляется запись в `dbo.Payments` со статусом `PENDING`.
- `GET /api/dev/sessions/{bookingId}` возвращает увеличившееся `remainingSeconds`.
- Если на том же компьютере есть будущая бронь с пересекающимся интервалом, HTTP 409; ни время, ни платёж не изменяются.
- Повторение с тем же `newEndUtc` должно быть отклонено (нельзя продлевать на ноль).
- Сессия, которая не началась или уже завершилась, не должна продлеваться.

## Проверка SQL

```sql
USE GamingClub;
SELECT BookingId, SeatId, StartUtc, EndUtc, Status FROM dbo.Bookings ORDER BY BookingId DESC;
SELECT BookingId, StartedUtc, EndsUtc, FinishedUtc FROM dbo.Sessions ORDER BY SessionId DESC;
SELECT BookingId, AmountRub, PaymentStatus FROM dbo.Payments ORDER BY PaymentId DESC;
```

## Известные ограничения

- Это учебный **Development-only** эндпоинт без авторизации. Не размещать сервер в открытой сети.
- Доплата определяется по текущей конфигурации тарифа как разница полной цены после и до продления. Для промышленной системы потребуется фиксировать снимок тарифных условий при бронировании и явно определять правила изменения цены.
- Реальная оплата, платежный шлюз, возвраты, права посетителя и история изменения тарифов не реализованы.
- Параллельное продление и новая бронь необходимо проверить на локальной SQL Server, в том числе с двумя клиентами.
