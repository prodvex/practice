# ER-модель GamingClub (вариант 18)

База данных: Microsoft SQL Server. Скрипт создания: `database/schema.sql`.

```mermaid
erDiagram
    Roles ||--o{ Users : assigns
    Halls ||--o{ Seats : contains
    Users ||--o{ Bookings : creates
    Seats ||--o{ Bookings : reserves
    Tariffs ||--o{ Bookings : prices
    Bookings ||--o| Sessions : starts
    Bookings ||--o{ Payments : invoices

    Roles {
        int RoleId PK
        nvarchar Code UK
        nvarchar DisplayName
    }
    Users {
        int UserId PK
        int RoleId FK
        nvarchar Login UK
        nvarchar PasswordHash
        nvarchar DisplayName
        datetime CreatedUtc
    }
    Halls {
        int HallId PK
        nvarchar Name UK
    }
    Seats {
        int SeatId PK
        int HallId FK
        int SeatNumber
        int PosX
        int PosY
        bool IsActive
    }
    Tariffs {
        int TariffId PK
        nvarchar Name UK
        varchar TariffType
        decimal PriceRub
        int PackageMinutes
        time StartLocalTime
        time EndLocalTime
        bool IsActive
    }
    Bookings {
        int BookingId PK
        int UserId FK
        int SeatId FK
        int TariffId FK
        datetime StartUtc
        datetime EndUtc
        varchar Status
    }
    Sessions {
        int SessionId PK
        int BookingId FK,UK
        datetime StartedUtc
        datetime EndsUtc
        datetime FinishedUtc
    }
    Payments {
        int PaymentId PK
        int BookingId FK
        decimal AmountRub
        datetime PaidUtc
        varchar PaymentStatus
    }
```

## Нормализация

- Справочники ролей, залов и тарифов выделены отдельно от фактов бронирования.
- Компьютер определяется `SeatId`, а номер места уникален в рамках зала.
- Пользователь и выбранный тариф связаны с бронью по внешним ключам.
- Сессия имеет уникальную ссылку на бронь, один сеанс на бронирование.
- Несколько оплат/доплат на одну бронь допускаются.

## Важные ограничения модели

1. Пересечение времени бронирований нельзя предотвратить только уникальным индексом — оно проверяется в серверной SQL-транзакции.
2. `Tariffs.StartLocalTime` и `EndLocalTime` хранят часы тарифа, но часовой пояс клуба в схеме пока отсутствует; прототип тарификации использует UTC, поэтому правила ночного тарифа нужно уточнить.
3. В схеме нет истории изменения тарифов и активных игровых мест, поэтому старые отчёты могут пересчитываться с текущими значениями.
4. `Payments` в прототипе означает начисления, настоящие денежные операции не подключены.
5. Авторизация и хеширование новых паролей будут добавлены отдельным этапом.

Для прикрепления к отчёту диаграмму необходимо экспортировать в PNG/SVG из Mermaid-редактора или оформить в Figma; пока здесь представлена исходная схема, а не графический файл.
