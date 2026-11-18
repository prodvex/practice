# Архитектура GamingClub — вариант 18

## Компоненты

```mermaid
flowchart LR
    A["GamingClub.Android\nKotlin · Android Studio"] -->|HTTPS в релизе / HTTP debug| API["GamingClub.API\nASP.NET Core · JWT"]
    W["GamingClub.Desktop\nC# · WPF · Visual Studio"] -->|HTTP/HTTPS + JWT| API
    API --> AUTH["AuthStore\nPBKDF2 + JWT"]
    API --> BOOK["BookingStore\nCancel / Extend / Sessions"]
    API --> PR["PricingCalculator\nПакеты · ночь · Happy Hours"]
    API --> REP["OccupancyCalculator\nCSV-отчёт"]
    AUTH --> DB[("Microsoft SQL Server\nGamingClub")]
    BOOK --> DB
    REP --> DB
```

**Единый источник истины:** клиенты получают места, бронирования, цены и таймер с сервера. Android не может напрямую изменять SQL. Запись в базу выполняет API.

## Последовательность бронирования

```mermaid
sequenceDiagram
    participant U as Посетитель
    participant M as Android
    participant A as ASP.NET Core API
    participant S as SQL Server
    U->>M: Выбирает ПК, тариф, UTC-время
    M->>A: POST /api/bookings (JWT)
    A->>A: Проверка роли и тарифа
    A->>S: BEGIN SERIALIZABLE + lock Seats
    A->>S: Проверить ACTIVE-интервалы
    alt Место свободно
      A->>S: INSERT Bookings + Payments(PENDING)
      A->>S: COMMIT
      A-->>M: Бронь и стоимость
    else Время занято
      A-->>M: HTTP 409 Conflict
    end
    M-->>U: Результат операции
```

## Отмена бронирования

```mermaid
sequenceDiagram
    participant C as Android / ПК
    participant API as ASP.NET Core API
    participant DB as SQL Server
    C->>API: POST /api/bookings/{id}/cancel (JWT)
    API->>API: Посетитель — только владелец
    API->>DB: BEGIN SERIALIZABLE, lock seat + booking
    API->>DB: Проверка ACTIVE, StartUtc > now, нет Session/PAID
    alt Разрешено
        API->>DB: Status=CANCELLED
        API->>DB: Payments(PENDING) => VOID
        API->>DB: COMMIT
        API-->>C: HTTP 200
    else Началось, оплачено или не ACTIVE
        API-->>C: HTTP 409
    end
```

## Структура данных и алгоритмы

- **8 таблиц**: Roles, Users, Halls, Seats, Tariffs, Bookings, Sessions, Payments; детали в `docs/er-diagram.md`.
- **Алгоритм тарификации**: расчёт стоимости пакета, ночного/льготного и почасового тарифа; разница при продлении.
- **Алгоритм загрузки зала**: объединение перекрывающихся интервалов и расчёт занятых компьютер-минут к ёмкости зала.
- **Блокировка двойной записи**: изоляция SERIALIZABLE и одинаковый порядок захвата блокировок при создании/продлении/отмене.
- **Безопасность**: PBKDF2-хеш паролей, JWT, проверка роли ADMIN и владельца бронирования.

## Технические ограничения прототипа

- HTTP для Android разрешён только в debug; production требует HTTPS.
- Время тарифных окон пока интерпретируется как UTC, хотя поля называются LocalTime. Требуется согласовать часовой пояс клуба.
- Прототип API имеет старые открытые маршруты `/api/dev/*`, доступные **только в режиме Development**. Не открывать такой сервер в сеть.
- Платёжный шлюз не реализован. `PENDING` — начисление, `VOID` — аннулированное до оплаты начисление, **не возврат денег**.
- Полноценные интеграционные тесты и UI-проверка не выполнены. Описанные схемы отражают исходный код, а не подтверждённую эксплуатацию.
