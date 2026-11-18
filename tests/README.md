# Тестирование GamingClub (вариант 18)

**Состояние на 18.11.2026:** .NET **85 PASS, 0 FAIL, 0 SKIPPED** на SQL Server 2022; Android JVM **31 PASS, 0 FAIL**. Покрытие строк общей .NET-сборки **85,05%** (734/863), ветвлений 72,25%. Реальный прогон: https://github.com/prodvex/practice/actions/runs/44367492709.

| Где | Написано | Назначение |
|---|---:|---|
| `tests/GamingClub.Tests/` | 51 unit-тест | Алгоритм тарификации, расширение сессии, загрузка зала, JWT/PBKDF2 и проверки входа |
| `tests/GamingClub.Tests/` | 15 HTTP-интеграционных тестов | ASP.NET Core WebApplicationFactory, JWT и права доступа, без SQL |
| `tests/GamingClub.Tests/` | 19 SQL-интеграционных тестов | Отдельная SQL Server база GamingClubTest_18, пересечения, транзакции и тарифы |
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

Для Android необходимы Android SDK, JDK 17 и Gradle/Android Studio. При появлении Gradle Wrapper рекомендуется использовать `gradlew.bat` вместо установленного Gradle. Сборка и все 31 тест Android реально прошли на GitHub Actions.

Дополнительные инструкции: `docs/api-integration-tests.md`, `docs/android-api-contract.md`, `docs/test-cases.md` и `docs/cancellation-verification.md`.

**До сдачи:** тестирование и покрытие ≥70% подтверждены; требуется ручная приёмка всего UI-сценария на Android/Windows, Production HTTPS и настоящие даты/подписи практики.
