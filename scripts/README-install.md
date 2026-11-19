# Установка GamingClub из архива

1. Скачайте ZIP-архив репозитория.
2. Откройте PowerShell от имени пользователя.
3. Запустите:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\Install-GamingClub.ps1 -ArchivePath .\practice.zip -InstallPath "$env:USERPROFILE\GamingClub" -RunTests -StartApi
```

Можно также запустить `scripts\install-gamingclub.bat`, передав ZIP первым аргументом.

Скрипт распаковывает архив, копирует проект в каталог установки, создаёт локальную конфигурацию, восстанавливает .NET-зависимости и при наличии `sqlcmd` создаёт базу из `database/schema.sql` и загружает синтетические данные из `database/seed.sql`. Секрет JWT генерируется локально и не записывается в Git.

Параметры `-SkipDatabase`, `-RunTests` и `-StartApi` управляют необязательными шагами. Для SQL Server Express используйте `-SqlServer "localhost\\SQLEXPRESS"`. Для другого экземпляра передайте собственное имя сервера.

Скрипт не устанавливает Windows, SQL Server, .NET SDK, Java или Android Studio. Если команда отсутствует, он записывает это в `install.log` и сообщает, какой компонент требуется установить.
