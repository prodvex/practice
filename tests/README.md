# Тестирование GamingClub (вариант 18)

**Состояние на 18.11.2026:** реальные GitHub Actions прогоны выполнены. .NET: **66 PASS, 0 FAIL, 13 SQL SKIPPED**, всего 79; Android: **31 PASS, 0 FAIL**. Покрытие общей .NET-сборки **30,01%**, ниже цели 70%. Подробный протокол: `docs/build-results-2026-11-18.md`.

| Где | Написано | Назначение |
|---|---:|---|
| `tests/GamingClub.Tests/` | 51 unit-тест | Алгоритм тарификации, расширение сессии, загрузка зала, JWT/PBKDF2 и проверки входа |
| `tests/GamingClub.Tests/` | 15 HTTP-интеграционных тестов | ASP.NET Core WebApplicationFactory, JWT и права доступа, без SQL |
| `tests/GamingClub.Tests/` | 13 SQL-интеграционных тестов | Отдельная SQL Server база GamingClubTest_18, пересечения, транзакции и тарифы |
| `mobile/GamingClub.Android/app/src/test/` | 31 JVM unit/contract-тест | Ввод UTC, устаревший логин, таймер, HTTP-клиент против MockWebServer |

## Команды для будущей проверки

.NET:
```powershell
dotnet test tests/GamingClub.Tests/GamingClub.Tests.csproj --collect:"XPlat Code Coverage"
```

SQL-интеграция пропускается без явно заданного `GAMINGCLUB_TEST_SQL_CONNECTION` и базы с точным именем `GamingClubTest_18`; подробности в `docs/sql-integration-tests.md`.

Android (из `mobile/GamingClub.Android`):
```powershell
gradle :app:testDebugUnitTest
```

Для Android необходимы Android SDK, JDK 17 и Gradle/Android Studio. При появлении Gradle Wrapper рекомендуется использовать `gradlew.bat` вместо установленного Gradle. Пока сборка не проводилась и версии совместимости не проверены.

Дополнительные инструкции: `docs/api-integration-tests.md`, `docs/android-api-contract.md`, `docs/test-cases.md` и `docs/cancellation-verification.md`.

**До сдачи:** API, WPF и Android Debug APK уже успешно собраны, а unit/HTTP/JVM тесты выполнены. Нужны: запустить 13 SQL-тестов, проверить Android→HTTP→SQL на эмуляторе, повысить общее покрытие .NET с измеренных 30,01% до ≥70%, сохранить реальные UI-скриншоты и проверить release-конфигурацию.
