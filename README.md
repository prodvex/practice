# GamingClub — вариант 18

Система бронирования игровых мест и учёта игровых сессий компьютерного клуба.

**Студент:** Медведев  
**Группа:** ИС-24П  
**Вариант:** 18  
**Номер в журнале:** 10  
**Индивидуальный seed:** `241810` (= 24 × 10000 + 18 × 100 + 10)

## Цель

Две клиентские версии, работающие с единым API:
- **ПК:** `GamingClub.Desktop` — C# / WPF / Visual Studio, кабинет администратора.
- **Android:** `GamingClub.Android` — Kotlin / Android Studio, интерфейс посетителя.
- **Сервер:** `GamingClub.API` — ASP.NET Core Web API.
- **База данных:** Microsoft SQL Server (SQL-скрипты в `database/`).

Мобильный клиент должен получать данные по HTTP API, без локальных заглушек в финальной версии.

## Основные возможности

Бронирование свободного компьютера по интервалу времени, отмена будущих броней, отображение схемы зала, запуск и продление сессии, управление тарифами (пакеты часов, ночной тариф, Happy Hours), расчёт стоимости и процента загрузки зала. Двойные бронирования должны блокироваться сервером.

## Структура

- `src/GamingClub.API/` — серверный API и алгоритмическое ядро
- `src/GamingClub.Desktop/` — WPF-клиент
- `mobile/GamingClub.Android/` — Android-клиент
- `database/` — SQL Server DDL и детерминированный seed
- `tests/` — тесты
- `docs/` — ТЗ, диаграммы, план и доказательства этапов
- `data/` — синтетические данные и экспорт отчётов

## Первый этап

1. В SQL Server Management Studio (SSMS) запустите `database/schema.sql`.
2. Затем запустите `database/seed.sql` для синтетических данных.
3. Для API требуется .NET SDK 8+ и строка подключения SQL Server: см. `docs/local-setup.md`. Для ПК-панели: `docs/admin-panel.md`.
4. В режиме Development Swagger доступен по `/swagger`; добавлены эндпоинты мест, тарифов, просмотра бронирований и локального тестового бронирования.
5. В `tests/` написаны 51 .NET unit-тест, 15 HTTP- и 13 SQL-интеграционных тестов. В Android-ветке написаны 31 JVM-тест для локального API-контракта и интерфейсных правил. **.NET тесты: 66 PASS, 13 SQL SKIPPED; Android: 31 PASS.** Измеренное покрытие .NET — 30,01%, ниже цели 70%. Подробности: `tests/README.md`, `docs/sql-integration-tests.md` и `docs/android-api-contract.md`.

**Статус 18.11.2026:** **ASP.NET Core API, WPF Windows x64 и Android Debug APK реально собраны** на GitHub Actions. .NET: 66 PASS, 0 FAIL, 13 SQL SKIPPED; Android JVM: 31 PASS, 0 FAIL. Пользовательский запуск приложений с SQL и эмулятором **ещё не проверен**. Подробности и артефакты: `docs/build-results-2026-11-18.md`. Android остаётся в `feature/android-client` (draft PR №1); Production/Release-сборка Android и полная SQL-интеграция не готовы.

## Материалы для защиты

- `docs/architecture.md` — компонентная схема и последовательности бронирования/отмены.
- **Figma:** https://www.figma.com/design/Gh6UqShvMOH9IWPghqE95P — шесть редактируемых Android-экранов; переходы Figma Prototype настроены между 6 экранами (37 реакций, просмотр Present ещё не проверен).
- `docs/design/` — оригинальные SVG-макеты, кликабельный локальный `prototype.html` и `figma-handoff.json` (не реальные скриншоты Android).
- `docs/android-api-contract.md` — контракт Android ↔ API, маршруты и 31 JVM-тест.
- `docs/build-results-2026-11-18.md` — реальные прогоны GitHub Actions, ссылки на APK/Windows-артефакты, ошибки компиляции и результаты покрытий.
- `docs/static-qa-review.md` — результаты ручной статической ревизии и ограничения.
- `.github/workflows/manual-build.yml` — запуск проверок через Actions **только вручную**.
- `docs/defense-demo.md` — сценарий будущей защиты и список доказательств.
- `docs/cancellation-verification.md` — отмена брони, миграция и сценарии проверки.

## Git

План: не менее 20 осмысленных коммитов **в разные дни**, как минимум две ветки, Pull Request/merge и тег `v1.0.0` к релизу. Исправление фактически обнаруженной ошибки оформляется отдельным `fix:`-коммитом. История не подделывается.

## Безопасность

Только синтетические тестовые данные. Не коммитить реальные пароли, токены или строки подключения с учётными данными. Использовать переменные окружения или User Secrets.
