# Фактические сборки GamingClub — 18 ноября 2026

Это **реальный протокол GitHub Actions**, а не прогноз или имитация работы.
Результаты относятся к отдельным веткам проверки; рабочие исходники сохранены в GitHub.

## Резервные исходники ДО сборок

- `backup/pre-build-2026-11-18-main` — неизменённая основа API/WPF до исправлений сборки.
- `backup/pre-build-2026-11-18-android` — неизменённая Android-ветка до исправлений сборки.

Изменения для исправления ошибок вносились только в рабочие и `ci/`-ветки.

## .NET 8, Windows (подтверждено успешно)

Успешный прогон: https://github.com/prodvex/practice/actions/runs/44364018823

| Компонент | Результат |
|---|---|
| ASP.NET Core API | **PASS** — `dotnet build` + `dotnet publish -c Release` |
| Windows WPF, win-x64 | **PASS** — `dotnet publish -c Release -r win-x64` |
| Unit и HTTP-интеграционные тесты | **PASS** — 66 прошли, 0 упали |
| SQL-интеграционные тесты | **SKIPPED** — 13 тестов без GamingClubTest_18 |
| Общее число обнаруженных .NET-тестов | 79 |

Получены артефакты GitHub Actions: `gamingclub-api-release`, `gamingclub-desktop-win-x64`, `gamingclub-dotnet-test-results`. В них доступны готовые бинарные файлы и отчёты xUnit/coverage.

### Реальные ошибки, найденные и исправленные

1. **CS0246 в WPF**: отсутствовало пространство имён для `HttpClient`, `HttpMethod`, `HttpRequestMessage`. Исправление: `src/GamingClub.Desktop/GlobalUsings.cs` (также внесено в `main`).
2. **CSV unit-тест**: `Csv_UsesSeedAndInvariantNumbers` ожидал `25.00`, а `CsvReport.Export` не гарантировал два знака после точки. Исправление: `ToString("F2", CultureInfo.InvariantCulture)` (также внесено в `main`).
3. После исправлений все три .NET задания GitHub Actions завершились успешно.

### Покрытие (реально измерено)

Отчёт `coverage.cobertura.xml` содержит:

- 259 покрытых строк из 863;
- **линейное покрытие всей .NET-сборки: 30,01%**;
- `PricingCalculator.cs`: примерно 95,8%;
- `OccupancyCalculator.cs`: примерно 86,7%;
- `ExtensionPolicy.cs`: примерно 93,8%.

Полное требование покрытия ≥70% **ещё не выполнено**. Большая часть SQL-зависимых классов не покрыта при пропуске SQL-интеграционных тестов. Эта метрика должна пересчитываться после реального SQL-прогона.

## Android (подтверждено успешно)

Успешный прогон: https://github.com/prodvex/practice/actions/runs/44364474968

| Компонент | Результат |
|---|---|
| Android SDK 35 + Java 17 + Gradle 8.10.2 | **PASS**, на GitHub Ubuntu runner |
| `gradle :app:assembleDebug` | **PASS**, получен `app-debug.apk` (849 788 байт) |
| `gradle :app:testDebugUnitTest` | **PASS**, все **31** JVM-тест пройдены |
| Результаты Android на эмуляторе | **NOT RUN** — APK ещё не установлен и UI не проверен |

APK и отчёты скомпилированы в отдельной ветке проверки `ci/build-check-android-20261118`. Артефакты GitHub Actions: `gamingclub-android-debug-apk`, `gamingclub-android-test-reports`.

### Реальные ошибки Android, найденные и исправленные

1. `android-actions/setup-android@v3` завершался ошибкой установки устаревшего SDK-пакета `tools`. Workflow переключён на встроенный `sdkmanager` Ubuntu runner, найденный по пути в `ANDROID_HOME`.
2. `sdkmanager` отсутствовал в `PATH`, поэтому путь вычисляется через `find "$ANDROID_HOME/cmdline-tools" -name sdkmanager`.
3. Kotlin-компиляция выявила `Unresolved reference 'LayoutParams'` в `MainActivity.kt`. Вместо `ScrollView.LayoutParams` теперь используется `android.widget.FrameLayout.LayoutParams`. Исправление сохранено также в `feature/android-client`.
4. После этих исправлений APK собрался, 31 JVM-тест прошёл.

**Внимание:** это **Debug APK**, использующий эмуляторный локальный HTTP API `10.0.2.2:5080`, не публикационная версия. Настоящее устройство/production требует HTTPS и иного адреса сервера.

## Что сборка не доказывает

- API и WPF успешно компилируются и публикуются, но их полноценный запуск вместе с SQL Server и проверка интерфейса ещё не проводились.
- APK нужно не только собрать, но и установить в эмулятор или на устройство; поведение UI пока не подтверждено.
- Настройка HTTPS/Production, реальные платежи и протокол ручного тестирования ещё не готовы.
- Ветку `feature/android-client` и draft PR №1 нельзя автоматически считать слитыми.
- Коммиты на разных календарных датах и дневник практики остаются отдельным требованием.

Для работы на Windows понадобятся .NET 8 Desktop Runtime и локально настроенный API/SQL Server. Запускать серверный Development API в публичной сети нельзя.
