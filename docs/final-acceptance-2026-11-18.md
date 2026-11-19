# GamingClub — акт технической готовности (18.11.2026)

Подтверждённые автоматизированные результаты на GitHub Actions:

| Требование | Фактический результат |
|---|---|
| База данных | SQL Server 2022, 8 таблиц GamingClubTest_18, успешно создана |
| Защита от двойного бронирования | Проверены транзакции, конфликты и параллельные запросы |
| Роли VISITOR/ADMIN и JWT | Проверены через реальный HTTP → ASP.NET Core → SQL |
| Сборка API .NET 8 | Release build и publish: **PASS** |
| Сборка WPF Windows x64 | Release publish: **PASS** |
| Сборка Android Debug APK | **PASS**, Java 17 / SDK 35 / Gradle 8.10.2 |
| Тестирование .NET | **85 PASS, 0 FAIL, 0 SKIPPED**, включая SQL/HTTP/SQL |
| Покрытие кода .NET | Строки **85,05%** (734/863); ветви **72,25%** |
| Тестирование Android JVM | **31 PASS, 0 FAIL, 0 SKIPPED** |
| Запуск на Android 15 Emulator | APK установлен, MainActivity открыта, реальные screenshots получены |
| Figma | 6 редактируемых экранов, 37 интерактивных связей Prototype |
| GitHub Pull Requests | [#1 Android](https://github.com/prodvex/practice/pull/1) и [#2 SQL](https://github.com/prodvex/practice/pull/2) **MERGED** |
| Исходные резервные копии | `backup/pre-build-2026-11-18-main`, `backup/pre-build-2026-11-18-android` (не изменены) |

## Прямые доказательства

- [Release API/WPF + первоначальные .NET-тесты](https://github.com/prodvex/practice/actions/runs/44364018823)
- [85 реальных .NET-тестов и Cobertura 85,05% с SQL Server](https://github.com/prodvex/practice/actions/runs/44367492709)
- [Android Debug APK и 31 JVM-тест после UI-исправлений](https://github.com/prodvex/practice/actions/runs/44369326160)
- [Android 15 Emulator: установка APK и screenshots](https://github.com/prodvex/practice/actions/runs/44366584601)
- [Шесть редактируемых экранов Figma](https://www.figma.com/design/Gh6UqShvMOH9IWPghqE95P)

## Не подтверждено и не должно выдаваться за готовое

- Полный пользовательский путь на Android-эмуляторе с одновременной работой API и SQL (серверные HTTP→SQL сценарии отдельно прошли).
- Запуск WPF UI на физическом Windows-компьютере и эксплуатация на реальном Android-телефоне.
- Публичный HTTPS/Production hosting, продакшен-секреты, платёжный шлюз, возвраты денег и политики часового пояса клуба.
- Просмотр Figma Prototype в режиме Present человеком (связи в документе настроены).
- Учебное требование делать коммиты в разные календарные дни и дневник четырёх недель: в репозитории 242 коммита, но реальные Git-коммиты выполнены в один день, даты задним числом не подменяются. Тег `v1.0.0` существует.
- Подписи, период практики, реквизиты образовательной организации и руководителя должны быть заполнены фактически.

Для защиты приложены исходники, результаты CI, итоговый Word-отчёт и прежние резервные копии. Отчёт доступен отдельным файлом в комплекте сдачи; бинарный DOCX в Git через текстовые API не загружался.
