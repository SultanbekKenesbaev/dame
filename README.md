# DailyGate

DailyGate — система ежедневного допуска сотрудников к рабочим компьютерам после обязательного теста. Репозиторий содержит API, фоновый worker, веб-админку, Windows-службу, WPF-клиент, WiX-инсталлятор и конфигурацию VPS.

## Что уже реализовано

- Рабочие сутки `04:00–03:59` в часовом поясе заведения.
- Сотрудники, группы, статусы, временные пароли и обязательная смена пароля.
- Администраторы `Admin` и наблюдатели `Viewer`, вход по логину и паролю, HttpOnly-сессии.
- Привязка персонального ПК по 15-минутному коду и ECDSA-ключу устройства.
- Банки вопросов, публикация, случайный неизменяемый набор и правила по группам.
- Статусы `Assigned`, `InProgress`, `Completed`, `TimedOut`, `Missed`, `EmergencyUnlocked`.
- Подписанный семидневный офлайн-кэш, локальная очередь и идемпотентные отправки.
- Аварийные коды на 15 минут, heartbeat, события безопасности и аудит.
- Dashboard, фильтруемые результаты, выбранные/пропущенные ответы, снимок устройства и CSV/XLSX-экспорт.
- Должности и группы, версии банков, перепривязка/отзыв устройств и управление Admin/Viewer.
- Windows Service, полноэкранный WPF-клиент, несколько мониторов и принудительный logoff в 04:00 в kiosk-режиме.
- Полный kiosk через Shell Launcher для Enterprise/Education/IoT и безопасный демонстрационный режим для Windows Pro.
- Docker Compose, Caddy HTTPS, PostgreSQL, отдельный worker и зашифрованный backup script.

## Структура

```text
app/                              React/TypeScript админка
src/DailyGate.Api/                ASP.NET Core API и миграции PostgreSQL
src/DailyGate.Shared/             Общие device/pipe-контракты
src/DailyGate.Windows.Service/    LocalSystem-служба и офлайн-хранилище
src/DailyGate.Windows.Client/     WPF kiosk-клиент
installer/                        WiX MSI и PowerShell provisioning
deploy/                           VPS Docker Compose, Caddy и backup
docs/                             Архитектура, API и инструкция пилота
```

## Локальная проверка

Требуются Node.js 22+, .NET 10 SDK и Docker.

```bash
npm ci
npm run build
NUGET_PACKAGES="$PWD/.nuget/packages" dotnet test src/DailyGate.Api.Tests/DailyGate.Api.Tests.csproj
NUGET_PACKAGES="$PWD/.nuget/packages" dotnet build DailyGate.slnx
```

Для полного запуска скопируйте `deploy/.env.example` в `deploy/.env`, замените все секреты и выполните из `deploy/`:

```bash
docker compose up --build
```

Если на VPS уже установлен Nginx и порты `80/443` заняты, используйте изолированный профиль:

```bash
docker compose -f docker-compose.yml -f docker-compose.vps.yml up -d --build db api worker web
```

Он публикует API и веб только на `127.0.0.1`; пример reverse-proxy находится в `deploy/nginx/dailygate.conf`.

После запуска админка доступна на домене из `DOMAIN`. API применяет миграции и создаёт первого администратора из `BOOTSTRAP_ADMIN_*`.

## Windows-сборка

Сборка MSI выполняется на Windows 10/11 с .NET 10 SDK и WiX:

```powershell
.\scripts\build-windows.ps1 -Configuration Release `
  -CertificateThumbprint "THUMBPRINT-КОРПОРАТИВНОГО-СЕРТИФИКАТА"
```

Для тестовой сборки на чистом Windows-ПК можно запустить загрузчик одной командой. Он скачает свежую ветку `main`, при необходимости установит локальную копию .NET 10 SDK, соберёт проект и сохранит MSI, клиент, службу, исходники и журнал сборки в новой папке `DailyGate-Build-*` на рабочем столе:

```powershell
$script = Join-Path $env:TEMP 'Get-DailyGate-Msi.ps1'
Invoke-WebRequest 'https://raw.githubusercontent.com/SultanbekKenesbaev/dame/main/scripts/Get-DailyGate-Msi.ps1' -OutFile $script
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script
```

Ещё проще: скачать [Build-DailyGate-MSI.cmd](https://raw.githubusercontent.com/SultanbekKenesbaev/dame/main/Build-DailyGate-MSI.cmd) и запустить двойным кликом. После успешной сборки проводник автоматически выделит готовый `DailyGate.Setup.msi`. Загрузчик не удаляет и не перезаписывает предыдущие сборки. Без `-CertificateThumbprint` результат предназначен только для теста.

После установки MSI администратор получает в веб-панели enrollment code и запускает:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File "$env:ProgramFiles\DailyGate\Tools\Provision-Device.ps1" `
  -ApiUrl "https://dailygate.example.com" `
  -EnrollmentCode "КОД-ИЗ-АДМИНКИ" `
  -DeviceName "Reception-01"
```

Скрипт сам определяет редакцию Windows. На Enterprise/Education/IoT он настраивает Shell Launcher и отдельный kiosk-профиль. На Windows Pro он включает демонстрационный режим для текущего пользователя: клиент запускается при входе, но не отключает Explorer, Task Manager и другие системные способы обхода. При технической ошибке доступна кнопка безопасного выхода. Перед первым пилотом обязательно прочитайте [инструкцию пилота](docs/WINDOWS-PILOT.md).

## Production-обязательства

- Подписать MSI и оба Windows executable доверенным code-signing сертификатом.
- Сохранить отдельную локальную учётную запись Windows-администратора и проверить её до kiosk-пилота.
- Настроить внешний `age`/`rclone` backup и ежемесячно проверять восстановление.
- Провести пилот на 5–10 физических ПК минимум семь рабочих дней.
- Не разворачивать принудительный logoff без письменного предупреждения сотрудников о сохранении документов до 04:00.

Подробности: [архитектура](docs/ARCHITECTURE.md), [API](docs/API.md), [Windows pilot](docs/WINDOWS-PILOT.md).

Backup-скрипт сохраняет 7 ежедневных, 8 недельных и 12 месячных копий. Проверочное восстановление выполняется только на отдельном стенде командой `CONFIRM_RESTORE=dailygate ./deploy/restore-postgres.sh /path/to/backup.sql.gz.age`.

На VPS systemd-шаблоны из `deploy/systemd/` запускают зашифрованный backup ежедневно в `02:30 UTC` с небольшим случайным смещением. Для настоящей отказоустойчивости `RCLONE_REMOTE` должен указывать на внешнее объектное хранилище, а не на каталог того же VPS.
