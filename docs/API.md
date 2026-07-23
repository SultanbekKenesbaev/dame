# DailyGate API v1

Базовый путь: `/api/v1`. Swagger доступен в Development на `/swagger`.

## Администратор

- `POST /admin/auth/login`, `POST /admin/auth/logout`, `GET /admin/auth/me`
- `POST /admin/auth/change-password`
- `GET|POST /admin/users`
- `GET|POST /admin/groups`
- `GET|POST /admin/employees`
- `PUT /admin/employees/{id}`
- `POST /admin/employees/{id}/reset-password`
- `GET /admin/devices`
- `POST /admin/devices/enrollment-codes`
- `POST /admin/devices/{id}/force-sync`
- `POST /admin/devices/{id}/reassign`
- `DELETE /admin/devices/{id}` — revoke, физического удаления нет
- `GET|POST /admin/test-banks`
- `GET /admin/test-banks/{id}`
- `POST /admin/test-banks/{id}/versions` — копирует банк в новый изменяемый черновик.
- `POST /admin/test-banks/{id}/questions`
- `POST /admin/test-banks/{id}/publish`
- `GET|POST /admin/test-rules`
- `DELETE /admin/test-rules/{id}` — выключает правило
- `GET /admin/analytics/dashboard`
- `GET /admin/analytics/results` — фильтры `from`, `to`, `employeeId`, `groupId`, `status`.
- `GET /admin/analytics/employees/{id}`
- `GET /admin/analytics/export.csv|xlsx`
- `GET /admin/audit`
- `GET|POST /admin/emergency-unlocks`

`Viewer` имеет доступ к аналитике, аудиту и read-only спискам сотрудников, групп и устройств. Управление тестами, аварийные коды и все мутации требуют `Admin`.

## Устройство

- `POST /device/enroll` — одноразовый enrollment code и публичный ключ.
- `POST /device/employee/login`
- `POST /device/employee/change-password`
- `GET /device/sync`
- `POST /device/heartbeat`
- `POST /device/events`
- `POST /device/submissions`
- `POST /device/emergency/verify`

После enrollment запросы содержат:

```text
X-Device-Id
X-Device-Timestamp
X-Device-Nonce
X-Device-Signature
```

Подписывается UTF-8 строка:

```text
HTTP_METHOD\nPATH_AND_QUERY\nUNIX_SECONDS\nNONCE\nLOWERCASE_SHA256_BODY
```

Алгоритм: ECDSA P-256 + SHA-256. Nonce одноразовый в десятиминутном окне.
