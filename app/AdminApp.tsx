"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";

type Role = "Admin" | "Viewer";
type User = { id: string; login: string; role: Role };
type NavKey = "dashboard" | "employees" | "results" | "tests" | "devices" | "audit" | "settings";
type Dashboard = {
  workday: string;
  totalEmployees: number;
  totalDevices: number;
  offlineDevices: number;
  counts: { status: string; count: number }[];
  events: { id: string; type: string; details?: string; receivedAt: string; device: string; employee: string }[];
};
type Group = { id: string; name: string; employeeCount: number };
type Employee = {
  id: string; fullName: string; login: string; position?: string; state: string; mustChangePassword: boolean;
  group?: { id: string; name: string }; device?: { id: string; name: string; lastSeenAt?: string };
};
type Device = {
  id: string; name: string; clientVersion: string; serviceVersion: string; lastSeenAt?: string;
  lastSyncAt?: string; offlineLeaseExpiresAt: string; forceSync: boolean; revoked: boolean;
  employee: { id: string; fullName: string; login: string };
};
type Bank = { id: string; name: string; description?: string; version: number; published: boolean; questionCount: number; activeQuestionCount: number };
type Rule = { id: string; name: string; questionCount: number; timeLimitMinutes: number; effectiveFrom: string; active: boolean; bank: { id: string; name: string; version: number }; group?: { id: string; name: string } };
type Audit = { id: string; actor: string; action: string; entityType: string; entityId?: string; createdAt: string };
type EmployeeDetails = { employee: { id: string; fullName: string; login: string; position?: string; state: string; group?: string; device?: string; lastSeenAt?: string }; history: { id: string; workday: string; status: string; startedAt?: string; completedAt?: string; durationSeconds?: number; wasOffline?: boolean; device?: string; clientVersion?: string; serviceVersion?: string; answers?: { questionId: string; question: string; selectedOptions: string[]; skipped: boolean }[] }[] };
type BankDetails = { id: string; name: string; description?: string; version: number; published: boolean; questions: { id: string; text: string; type: string; active: boolean; options: { id: string; text: string }[] }[] };
type ResultRow = { id: string; workday: string; employee: { id: string; fullName: string; login: string }; group?: string; status: string; startedAt?: string; completedAt?: string; durationSeconds?: number; wasOffline?: boolean; device?: string; clientVersion?: string; serviceVersion?: string };
type AdminAccount = { id: string; login: string; role: Role; active: boolean; createdAt: string; lastLoginAt?: string };

const navigation: { key: NavKey; label: string; mark: string }[] = [
  { key: "dashboard", label: "Обзор", mark: "01" },
  { key: "employees", label: "Сотрудники", mark: "02" },
  { key: "results", label: "Результаты", mark: "03" },
  { key: "tests", label: "Тесты", mark: "04" },
  { key: "devices", label: "Устройства", mark: "05" },
  { key: "audit", label: "Аудит", mark: "06" },
  { key: "settings", label: "Настройки", mark: "07" },
];

async function api<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`/api/v1${path}`, {
    credentials: "include",
    headers: { "Content-Type": "application/json", ...(options?.headers ?? {}) },
    ...options,
  });
  if (response.status === 401) throw new Error("AUTH_REQUIRED");
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { message?: string } | null;
    throw new Error(body?.message ?? `Ошибка сервера: ${response.status}`);
  }
  if (response.status === 204) return undefined as T;
  return response.json() as Promise<T>;
}

function formatDate(value?: string, withTime = true) {
  if (!value) return "—";
  return new Intl.DateTimeFormat("ru-RU", withTime
    ? { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" }
    : { day: "2-digit", month: "long", year: "numeric" }).format(new Date(value));
}

function initials(name: string) {
  return name.split(" ").filter(Boolean).slice(0, 2).map((part) => part[0]).join("").toUpperCase();
}

export function AdminApp() {
  const [user, setUser] = useState<User | null>(null);
  const [checking, setChecking] = useState(true);
  const [active, setActive] = useState<NavKey>("dashboard");

  useEffect(() => {
    api<User>("/admin/auth/me").then(setUser).catch(() => setUser(null)).finally(() => setChecking(false));
  }, []);

  if (checking) return <div className="boot"><div className="brand-mark">DG</div><b>DailyGate</b><p>Защищённая панель · проверяем сессию…</p></div>;
  if (!user) return <Login onSuccess={setUser} />;

  const logout = async () => {
    await api<void>("/admin/auth/logout", { method: "POST" }).catch(() => undefined);
    setUser(null);
  };

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand"><span className="brand-mark">DG</span><span><b>DailyGate</b><small>Ежедневный допуск</small></span></div>
        <nav aria-label="Основная навигация">
          {navigation.filter((item) => user.role === "Admin" || !["tests", "settings"].includes(item.key)).map((item) => <button key={item.key} className={active === item.key ? "nav-item active" : "nav-item"} onClick={() => setActive(item.key)}>
            <span>{item.mark}</span>{item.label}
          </button>)}
        </nav>
        <div className="sidebar-foot">
          <div className="live-dot"><i /> Сервер подключён</div>
          <button className="profile" onClick={logout} title="Выйти">
            <span className="avatar">{initials(user.login)}</span><span><b>{user.login}</b><small>{user.role === "Admin" ? "Администратор" : "Наблюдатель"}</small></span><em>↗</em>
          </button>
        </div>
      </aside>
      <main className="main">
        {active === "dashboard" && <DashboardView />}
        {active === "employees" && <EmployeesView canEdit={user.role === "Admin"} />}
        {active === "results" && <ResultsView />}
        {active === "tests" && <TestsView canEdit={user.role === "Admin"} />}
        {active === "devices" && <DevicesView canEdit={user.role === "Admin"} />}
        {active === "audit" && <AuditView />}
        {active === "settings" && <SettingsView />}
      </main>
    </div>
  );
}

function Login({ onSuccess }: { onSuccess: (user: User) => void }) {
  const [login, setLogin] = useState(""); const [password, setPassword] = useState("");
  const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const submit = async (event: FormEvent) => {
    event.preventDefault(); setBusy(true); setError("");
    try {
      const user = await api<User>("/admin/auth/login", { method: "POST", body: JSON.stringify({ login, password }) });
      onSuccess(user);
    } catch { setError("Не удалось войти. Проверьте логин и пароль."); } finally { setBusy(false); }
  };
  return <main className="login-page">
    <section className="login-story">
      <div className="brand light"><span className="brand-mark">DG</span><span><b>DailyGate</b><small>Ежедневный допуск</small></span></div>
      <div className="story-copy"><p className="eyebrow">Контроль без лишнего шума</p><h1>Рабочий день начинается с уверенности.</h1><p>Ежедневные тесты, состояние компьютеров и история допуска — в одной защищённой панели.</p></div>
      <div className="story-metric"><span>04:00</span><p>Единая граница рабочего дня<br />для всей команды</p></div>
    </section>
    <section className="login-panel"><form onSubmit={submit} className="login-card">
      <p className="eyebrow">Закрытая зона</p><h2>Вход в админ-панель</h2><p className="muted">Используйте данные администратора или наблюдателя.</p>
      <label>Логин<input autoFocus required autoComplete="username" value={login} onChange={(e) => setLogin(e.target.value)} placeholder="admin" /></label>
      <label>Пароль<input required type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder="••••••••••••" /></label>
      {error && <div className="form-error" role="alert">{error}</div>}
      <button className="primary wide" disabled={busy}>{busy ? "Проверяем…" : "Войти в систему"}</button>
      <small className="security-note">Сессия защищена HTTPS и HttpOnly cookie.</small>
    </form></section>
  </main>;
}

function PageHeader({ eyebrow, title, action }: { eyebrow: string; title: string; action?: React.ReactNode }) {
  return <header className="page-header"><div><p className="eyebrow">{eyebrow}</p><h1>{title}</h1></div>{action}</header>;
}

function StatusPill({ status }: { status: string }) {
  const map: Record<string, string> = { Completed: "Завершён", TimedOut: "Время вышло", Missed: "Пропущен", EmergencyUnlocked: "Аварийный доступ", Assigned: "Ожидает", InProgress: "В процессе", Active: "Активен", Disabled: "Заблокирован", Archived: "Архив", Revoked: "Отозван", Offline: "Не в сети" };
  return <span className={`status status-${status.toLowerCase()}`}>{map[status] ?? status}</span>;
}

function DashboardView() {
  const [data, setData] = useState<Dashboard | null>(null); const [error, setError] = useState("");
  const load = useCallback(() => api<Dashboard>("/admin/analytics/dashboard").then(setData).catch((e) => setError(e.message)), []);
  useEffect(() => { load(); const timer = setInterval(load, 60_000); return () => clearInterval(timer); }, [load]);
  const count = (status: string) => data?.counts.find((item) => item.status === status)?.count ?? 0;
  const completed = count("Completed") + count("TimedOut");
  const completion = data?.totalEmployees ? Math.round(completed / data.totalEmployees * 100) : 0;
  return <>
    <PageHeader eyebrow={`Рабочий день ${data?.workday ?? "—"}`} title="Сегодняшний допуск" action={<button className="ghost" onClick={load}>Обновить данные</button>} />
    {error && <ErrorBanner message={error} />}
    <section className="hero-grid">
      <article className="completion-card"><div><p>Команда завершила</p><strong>{completion}<sup>%</sup></strong><span>{completed} из {data?.totalEmployees ?? 0} сотрудников</span></div><div className="ring" style={{ "--value": `${completion * 3.6}deg` } as React.CSSProperties}><b>{completion}%</b></div></article>
      <article className="metric-card"><span className="metric-index">01</span><p>Ожидают тест</p><strong>{count("Assigned") + count("InProgress")}</strong><small>текущий рабочий день</small></article>
      <article className="metric-card warning"><span className="metric-index">02</span><p>ПК вне сети</p><strong>{data?.offlineDevices ?? 0}</strong><small>из {data?.totalDevices ?? 0} устройств</small></article>
    </section>
    <section className="content-grid">
      <article className="panel"><div className="panel-head"><div><p className="eyebrow">Статусы</p><h2>Сводка прохождения</h2></div></div>
        <div className="status-stack">{["Completed", "TimedOut", "Assigned", "InProgress", "Missed", "EmergencyUnlocked"].map((status) => <div key={status}><StatusPill status={status} /><b>{count(status)}</b></div>)}</div>
      </article>
      <article className="panel"><div className="panel-head"><div><p className="eyebrow">Последние 24 часа</p><h2>Требуют внимания</h2></div></div>
        <div className="event-list">{data?.events.length ? data.events.map((event) => <div className="event" key={event.id}><span className="event-mark">!</span><div><b>{event.employee}</b><p>{event.details ?? event.type} · {event.device}</p></div><time>{formatDate(event.receivedAt)}</time></div>) : <Empty text="Критических событий нет" />}</div>
      </article>
    </section>
  </>;
}

function ResultsView() {
  const today = new Date().toISOString().slice(0, 10);
  const monthAgo = new Date(Date.now() - 30 * 86_400_000).toISOString().slice(0, 10);
  const [filters, setFilters] = useState({ from: monthAgo, to: today, employeeId: "", groupId: "", status: "" });
  const [rows, setRows] = useState<ResultRow[]>([]); const [employees, setEmployees] = useState<Employee[]>([]); const [groups, setGroups] = useState<Group[]>([]); const [error, setError] = useState("");
  const query = useCallback(() => { const params = new URLSearchParams(); Object.entries(filters).forEach(([key, value]) => { if (value) params.set(key, value); }); return params.toString(); }, [filters]);
  const load = useCallback(() => api<ResultRow[]>(`/admin/analytics/results?${query()}`).then(setRows).catch((e) => setError(e.message)), [query]);
  useEffect(() => { Promise.all([api<Employee[]>("/admin/employees"), api<Group[]>("/admin/groups")]).then(([people, teams]) => { setEmployees(people); setGroups(teams); }).catch((e) => setError(e.message)); }, []);
  useEffect(() => { load(); }, [load]);
  return <>
    <PageHeader eyebrow="Фильтры, история и экспорт" title="Результаты тестирования" action={<div className="actions"><a className="ghost button-link" href={`/api/v1/admin/analytics/export.csv?${query()}`}>CSV</a><a className="primary button-link" href={`/api/v1/admin/analytics/export.xlsx?${query()}`}>XLSX</a></div>} />
    {error && <ErrorBanner message={error} />}
    <div className="filter-bar"><label>С<input type="date" value={filters.from} onChange={(e) => setFilters({ ...filters, from: e.target.value })} /></label><label>По<input type="date" value={filters.to} onChange={(e) => setFilters({ ...filters, to: e.target.value })} /></label><label>Сотрудник<select value={filters.employeeId} onChange={(e) => setFilters({ ...filters, employeeId: e.target.value })}><option value="">Все</option>{employees.map((employee) => <option key={employee.id} value={employee.id}>{employee.fullName}</option>)}</select></label><label>Группа<select value={filters.groupId} onChange={(e) => setFilters({ ...filters, groupId: e.target.value })}><option value="">Все</option>{groups.map((group) => <option key={group.id} value={group.id}>{group.name}</option>)}</select></label><label>Статус<select value={filters.status} onChange={(e) => setFilters({ ...filters, status: e.target.value })}><option value="">Все</option>{["Completed", "TimedOut", "Missed", "EmergencyUnlocked", "InProgress", "Assigned"].map((status) => <option key={status} value={status}>{status}</option>)}</select></label></div>
    <div className="table-wrap"><table><thead><tr><th>Дата</th><th>Сотрудник</th><th>Статус</th><th>Начало / завершение</th><th>Режим</th><th>Устройство</th></tr></thead><tbody>{rows.map((row) => <tr key={row.id}><td>{row.workday}</td><td><span className="person"><span className="avatar">{initials(row.employee.fullName)}</span><span><b>{row.employee.fullName}</b><small>{row.group ?? "Без группы"}</small></span></span></td><td><StatusPill status={row.status} /></td><td>{formatDate(row.startedAt)}<br /><small>{formatDate(row.completedAt)}</small></td><td>{row.wasOffline == null ? "—" : row.wasOffline ? "Офлайн" : "Онлайн"}</td><td>{row.device ?? "—"}<br /><small>{row.clientVersion ? `client ${row.clientVersion}` : ""}</small></td></tr>)}</tbody></table>{rows.length === 0 && <Empty text="По выбранным фильтрам результатов нет" />}</div>
  </>;
}

function EmployeesView({ canEdit }: { canEdit: boolean }) {
  const [employees, setEmployees] = useState<Employee[]>([]); const [groups, setGroups] = useState<Group[]>([]);
  const [search, setSearch] = useState(""); const [open, setOpen] = useState(false); const [error, setError] = useState("");
  const [editing, setEditing] = useState<Employee | null>(null); const [groupOpen, setGroupOpen] = useState(false);
  const [code, setCode] = useState<{ code: string; expiresAt: string; employee: string } | null>(null); const [details, setDetails] = useState<EmployeeDetails | null>(null);
  const load = useCallback(() => Promise.all([api<Employee[]>("/admin/employees"), api<Group[]>("/admin/groups")]).then(([e, g]) => { setEmployees(e); setGroups(g); }).catch((e) => setError(e.message)), []);
  useEffect(() => { load(); }, [load]);
  const filtered = employees.filter((employee) => `${employee.fullName} ${employee.login}`.toLowerCase().includes(search.toLowerCase()));
  const enrollment = async (employee: Employee) => { try { const result = await api<{ code: string; expiresAt: string }>("/admin/devices/enrollment-codes", { method: "POST", body: JSON.stringify({ employeeId: employee.id }) }); setCode({ ...result, employee: employee.fullName }); } catch (e) { setError((e as Error).message); } };
  const showDetails = async (employee: Employee) => { try { setDetails(await api<EmployeeDetails>(`/admin/analytics/employees/${employee.id}`)); } catch (e) { setError((e as Error).message); } };
  return <>
    <PageHeader eyebrow={`${employees.length} человек в системе`} title="Сотрудники" action={canEdit && <div className="actions"><button className="ghost" onClick={() => setGroupOpen(true)}>+ Группа</button><button className="primary" onClick={() => setOpen(true)}>+ Добавить сотрудника</button></div>} />
    {error && <ErrorBanner message={error} />}
    <div className="toolbar"><input className="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Поиск по имени или логину" /><span>Активных: {employees.filter((x) => x.state === "Active").length}</span></div>
    <div className="table-wrap"><table><thead><tr><th>Сотрудник</th><th>Группа</th><th>Статус</th><th>Устройство</th><th>Последняя связь</th><th></th></tr></thead><tbody>
      {filtered.map((employee) => <tr key={employee.id}><td><button className="person person-button" onClick={() => showDetails(employee)}><span className="avatar">{initials(employee.fullName)}</span><span><b>{employee.fullName}</b><small>@{employee.login}{employee.position ? ` · ${employee.position}` : ""}</small></span></button></td><td>{employee.group?.name ?? "Все сотрудники"}</td><td><StatusPill status={employee.state} /></td><td>{employee.device?.name ?? <span className="muted">Не привязано</span>}</td><td>{formatDate(employee.device?.lastSeenAt)}</td><td>{canEdit && <div className="row-actions"><button className="ghost small" onClick={() => setEditing(employee)}>Изменить</button>{!employee.device && <button className="ghost small" onClick={() => enrollment(employee)}>Подключить ПК</button>}</div>}</td></tr>)}
    </tbody></table>{filtered.length === 0 && <Empty text="Сотрудники не найдены" />}</div>
    {open && <EmployeeModal groups={groups} onClose={() => setOpen(false)} onSaved={() => { setOpen(false); load(); }} />}
    {editing && <EmployeeEditModal employee={editing} groups={groups} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); load(); }} />}
    {groupOpen && <GroupModal onClose={() => setGroupOpen(false)} onSaved={() => { setGroupOpen(false); load(); }} />}
    {code && <Modal title={`Подключение · ${code.employee}`} onClose={() => setCode(null)}><div className="code-box"><small>Код действует до {formatDate(code.expiresAt)}</small><strong>{code.code}</strong><p>Техник вводит этот код один раз в Provision-Device.ps1 на компьютере сотрудника.</p></div></Modal>}
    {details && <EmployeeDetailsModal details={details} onClose={() => setDetails(null)} />}
  </>;
}

function EmployeeEditModal({ employee, groups, onClose, onSaved }: { employee: Employee; groups: Group[]; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({ fullName: employee.fullName, position: employee.position ?? "", groupId: employee.group?.id ?? "", state: employee.state }); const [password, setPassword] = useState(""); const [error, setError] = useState("");
  const submit = async (event: FormEvent) => { event.preventDefault(); try { await api(`/admin/employees/${employee.id}`, { method: "PUT", body: JSON.stringify({ ...form, groupId: form.groupId || null }) }); if (password) await api(`/admin/employees/${employee.id}/reset-password`, { method: "POST", body: JSON.stringify({ temporaryPassword: password }) }); onSaved(); } catch (e) { setError((e as Error).message); } };
  return <Modal title={`Изменить · ${employee.fullName}`} onClose={onClose}><form className="stack-form" onSubmit={submit}><label>ФИО<input required value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} /></label><label>Должность<input maxLength={160} value={form.position} onChange={(e) => setForm({ ...form, position: e.target.value })} /></label><label>Группа<select value={form.groupId} onChange={(e) => setForm({ ...form, groupId: e.target.value })}><option value="">Без группы</option>{groups.map((group) => <option key={group.id} value={group.id}>{group.name}</option>)}</select></label><label>Статус<select value={form.state} onChange={(e) => setForm({ ...form, state: e.target.value })}><option value="Active">Активен</option><option value="Disabled">Заблокирован</option><option value="Archived">Архив</option></select></label><label>Новый временный пароль <small>(необязательно)</small><input type="password" minLength={12} value={password} onChange={(e) => setPassword(e.target.value)} placeholder="Оставьте пустым без сброса" /></label>{error && <div className="form-error">{error}</div>}<button className="primary">Сохранить</button></form></Modal>;
}

function GroupModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [name, setName] = useState(""); const [error, setError] = useState("");
  const submit = async (event: FormEvent) => { event.preventDefault(); try { await api("/admin/groups", { method: "POST", body: JSON.stringify({ name }) }); onSaved(); } catch (e) { setError((e as Error).message); } };
  return <Modal title="Новая группа" onClose={onClose}><form className="stack-form" onSubmit={submit}><label>Название<input required value={name} onChange={(e) => setName(e.target.value)} /></label>{error && <div className="form-error">{error}</div>}<button className="primary">Создать группу</button></form></Modal>;
}

function EmployeeModal({ groups, onClose, onSaved }: { groups: Group[]; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({ fullName: "", login: "", position: "", temporaryPassword: "", groupId: "" }); const [error, setError] = useState("");
  const submit = async (event: FormEvent) => { event.preventDefault(); setError(""); try { await api("/admin/employees", { method: "POST", body: JSON.stringify({ ...form, groupId: form.groupId || null }) }); onSaved(); } catch (e) { setError((e as Error).message); } };
  return <Modal title="Новый сотрудник" onClose={onClose}><form className="stack-form" onSubmit={submit}>
    <label>ФИО<input required value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} /></label>
    <label>Логин<input required value={form.login} onChange={(e) => setForm({ ...form, login: e.target.value })} /></label>
    <label>Должность<input maxLength={160} value={form.position} onChange={(e) => setForm({ ...form, position: e.target.value })} /></label>
    <label>Временный пароль<input required type="password" minLength={12} value={form.temporaryPassword} onChange={(e) => setForm({ ...form, temporaryPassword: e.target.value })} placeholder="Минимум 12 символов" /></label>
    <label>Группа<select value={form.groupId} onChange={(e) => setForm({ ...form, groupId: e.target.value })}><option value="">Все сотрудники</option>{groups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}</select></label>
    {error && <div className="form-error">{error}</div>}<button className="primary">Создать сотрудника</button>
  </form></Modal>;
}

function TestsView({ canEdit }: { canEdit: boolean }) {
  const [banks, setBanks] = useState<Bank[]>([]); const [rules, setRules] = useState<Rule[]>([]); const [groups, setGroups] = useState<Group[]>([]); const [open, setOpen] = useState<"bank" | "rule" | null>(null); const [editingBank, setEditingBank] = useState<string | null>(null); const [error, setError] = useState("");
  const load = useCallback(() => Promise.all([api<Bank[]>("/admin/test-banks"), api<Rule[]>("/admin/test-rules"), api<Group[]>("/admin/groups")]).then(([b, r, g]) => { setBanks(b); setRules(r); setGroups(g); }).catch((e) => setError(e.message)), []);
  useEffect(() => { load(); }, [load]);
  const disableRule = async (id: string) => { try { await api(`/admin/test-rules/${id}`, { method: "DELETE" }); load(); } catch (e) { setError((e as Error).message); } };
  return <>
    <PageHeader eyebrow="Банки и ежедневные правила" title="Тестирование" action={canEdit && <div className="actions"><button className="ghost" onClick={() => setOpen("bank")}>+ Новый банк</button><button className="primary" onClick={() => setOpen("rule")}>+ Новое правило</button></div>} />
    {error && <ErrorBanner message={error} />}
    <section className="content-grid"><article className="panel"><div className="panel-head"><div><p className="eyebrow">Контент</p><h2>Банки вопросов</h2></div></div>
      <div className="card-list">{banks.map((bank) => <button className="list-card button-card" key={bank.id} onClick={() => setEditingBank(bank.id)}><span><b>{bank.name}</b><p>{bank.description || "Без описания"}</p></span><span className="list-meta"><span>v{bank.version}</span><span>{bank.activeQuestionCount} вопросов</span><StatusPill status={bank.published ? "Active" : "Черновик"} /></span></button>)}{banks.length === 0 && <Empty text="Создайте первый банк вопросов" />}</div>
    </article><article className="panel"><div className="panel-head"><div><p className="eyebrow">Расписание</p><h2>Активные правила</h2></div></div>
      <div className="card-list">{rules.map((rule) => <div className="list-card" key={rule.id}><div><b>{rule.name}</b><p>{rule.bank.name} · {rule.group?.name ?? "все сотрудники"}</p></div><div className="list-meta"><span>{rule.questionCount} вопросов</span><span>{rule.timeLimitMinutes} мин</span><span>с {rule.effectiveFrom}</span>{rule.active ? <StatusPill status="Active" /> : <StatusPill status="Archived" />}{canEdit && rule.active && <button className="danger-link" onClick={() => disableRule(rule.id)}>Отключить</button>}</div></div>)}{rules.length === 0 && <Empty text="Правила пока не настроены" />}</div>
    </article></section>
    {open === "bank" && <BankModal onClose={() => setOpen(null)} onSaved={() => { setOpen(null); load(); }} />}
    {open === "rule" && <RuleModal banks={banks.filter((x) => x.published)} groups={groups} onClose={() => setOpen(null)} onSaved={() => { setOpen(null); load(); }} />}
    {editingBank && <BankEditor bankId={editingBank} canEdit={canEdit} onClose={() => setEditingBank(null)} onChanged={load} />}
  </>;
}

function BankModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [name, setName] = useState(""); const [description, setDescription] = useState(""); const [error, setError] = useState("");
  const submit = async (event: FormEvent) => { event.preventDefault(); try { await api("/admin/test-banks", { method: "POST", body: JSON.stringify({ name, description }) }); onSaved(); } catch (e) { setError((e as Error).message); } };
  return <Modal title="Новый банк вопросов" onClose={onClose}><form className="stack-form" onSubmit={submit}><label>Название<input required value={name} onChange={(e) => setName(e.target.value)} /></label><label>Описание<textarea value={description} onChange={(e) => setDescription(e.target.value)} /></label>{error && <div className="form-error">{error}</div>}<button className="primary">Создать банк</button></form></Modal>;
}

function BankEditor({ bankId, canEdit, onClose, onChanged }: { bankId: string; canEdit: boolean; onClose: () => void; onChanged: () => void }) {
  const [bank, setBank] = useState<BankDetails | null>(null); const [text, setText] = useState(""); const [type, setType] = useState("SingleChoice"); const [options, setOptions] = useState(""); const [error, setError] = useState("");
  const load = useCallback(() => api<BankDetails>(`/admin/test-banks/${bankId}`).then(setBank).catch((e) => setError(e.message)), [bankId]); useEffect(() => { load(); }, [load]);
  const addQuestion = async (event: FormEvent) => { event.preventDefault(); try { const values = options.split(/\n|,/).map((x) => x.trim()).filter(Boolean); await api(`/admin/test-banks/${bankId}/questions`, { method: "POST", body: JSON.stringify({ text, type, options: values }) }); setText(""); setOptions(""); await load(); onChanged(); } catch (e) { setError((e as Error).message); } };
  const publish = async () => { try { await api(`/admin/test-banks/${bankId}/publish`, { method: "POST", body: "{}" }); await load(); onChanged(); } catch (e) { setError((e as Error).message); } };
  const cloneVersion = async () => { try { await api(`/admin/test-banks/${bankId}/versions`, { method: "POST", body: "{}" }); onChanged(); onClose(); } catch (e) { setError((e as Error).message); } };
  return <Modal title={bank?.name ?? "Банк вопросов"} onClose={onClose}><div className="bank-editor">
    {error && <div className="form-error">{error}</div>}<div className="question-list">{bank?.questions.map((q, index) => <article className="question-row" key={q.id}><span>{String(index + 1).padStart(2, "0")}</span><div><b>{q.text}</b><p>{q.type === "SingleChoice" ? "Один ответ" : "Несколько ответов"} · {q.options.map((o) => o.text).join(" / ")}</p></div></article>)}{bank?.questions.length === 0 && <Empty text="Добавьте минимум один вопрос" />}</div>
    {canEdit && bank && !bank.published && <form className="stack-form inset" onSubmit={addQuestion}><h3>Новый вопрос</h3><label>Текст вопроса<textarea required value={text} onChange={(e) => setText(e.target.value)} /></label><label>Тип<select value={type} onChange={(e) => setType(e.target.value)}><option value="SingleChoice">Один вариант</option><option value="MultipleChoice">Несколько вариантов</option></select></label><label>Варианты — по строке или через запятую<textarea required value={options} onChange={(e) => setOptions(e.target.value)} placeholder={"Да\nНет"} /></label><div className="actions"><button className="ghost" type="submit">Добавить вопрос</button><button className="primary" type="button" onClick={publish} disabled={!bank.questions.length}>Опубликовать банк</button></div></form>}
    {bank?.published && <div className="published-note"><StatusPill status="Active" /><span>Опубликованный банк неизменяем. Новая версия копирует вопросы в отдельный черновик.</span>{canEdit && <button className="ghost small" onClick={cloneVersion}>Создать версию v{bank.version + 1}</button>}</div>}
  </div></Modal>;
}

function RuleModal({ banks, groups, onClose, onSaved }: { banks: Bank[]; groups: Group[]; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({ name: "", questionBankId: banks[0]?.id ?? "", employeeGroupId: "", questionCount: 10, timeLimitMinutes: 15, effectiveFrom: new Date().toISOString().slice(0, 10) }); const [error, setError] = useState("");
  const submit = async (event: FormEvent) => { event.preventDefault(); try { await api("/admin/test-rules", { method: "POST", body: JSON.stringify({ ...form, employeeGroupId: form.employeeGroupId || null }) }); onSaved(); } catch (e) { setError((e as Error).message); } };
  return <Modal title="Новое ежедневное правило" onClose={onClose}><form className="stack-form" onSubmit={submit}>
    <label>Название<input required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></label><label>Опубликованный банк<select required value={form.questionBankId} onChange={(e) => setForm({ ...form, questionBankId: e.target.value })}><option value="">Выберите банк</option>{banks.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}</select></label>
    <label>Группа<select value={form.employeeGroupId} onChange={(e) => setForm({ ...form, employeeGroupId: e.target.value })}><option value="">Все сотрудники</option>{groups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}</select></label>
    <div className="form-row"><label>Вопросов<input type="number" min={1} max={100} value={form.questionCount} onChange={(e) => setForm({ ...form, questionCount: Number(e.target.value) })} /></label><label>Минут<input type="number" min={1} max={180} value={form.timeLimitMinutes} onChange={(e) => setForm({ ...form, timeLimitMinutes: Number(e.target.value) })} /></label></div>
    <label>Действует с<input type="date" value={form.effectiveFrom} onChange={(e) => setForm({ ...form, effectiveFrom: e.target.value })} /></label>{error && <div className="form-error">{error}</div>}<button className="primary">Создать правило</button>
  </form></Modal>;
}

function DevicesView({ canEdit }: { canEdit: boolean }) {
  const [devices, setDevices] = useState<Device[]>([]); const [employees, setEmployees] = useState<Employee[]>([]); const [code, setCode] = useState<{ code: string; expiresAt: string; title: string } | null>(null); const [reassigning, setReassigning] = useState<Device | null>(null); const [error, setError] = useState("");
  const load = useCallback(() => Promise.all([api<Device[]>("/admin/devices"), api<Employee[]>("/admin/employees")]).then(([items, people]) => { setDevices(items); setEmployees(people); }).catch((e) => setError(e.message)), []); useEffect(() => { load(); }, [load]);
  const online = (device: Device) => Boolean(!device.revoked && device.lastSeenAt && Date.now() - new Date(device.lastSeenAt).getTime() < 15 * 60_000);
  const emergency = async (device: Device) => { try { const result = await api<{ code: string; expiresAt: string }>("/admin/emergency-unlocks", { method: "POST", body: JSON.stringify({ deviceId: device.id, workday: null }) }); setCode({ ...result, title: `Аварийный код · ${device.name}` }); } catch (e) { setError((e as Error).message); } };
  const revoke = async (device: Device) => { if (!window.confirm(`Отозвать устройство ${device.name}? Доступ будет заблокирован.`)) return; try { await api(`/admin/devices/${device.id}`, { method: "DELETE" }); load(); } catch (e) { setError((e as Error).message); } };
  return <>
    <PageHeader eyebrow={`${devices.filter(online).length} из ${devices.length} в сети`} title="Рабочие компьютеры" />{error && <ErrorBanner message={error} />}
    <div className="device-grid">{devices.map((device) => <article className="device-card" key={device.id}><div className="device-top"><span className={online(device) ? "device-icon online" : "device-icon"}>PC</span><StatusPill status={device.revoked ? "Revoked" : online(device) ? "Active" : "Offline"} /></div><h3>{device.name}</h3><p>{device.employee.fullName}</p><dl><div><dt>Последняя связь</dt><dd>{formatDate(device.lastSeenAt)}</dd></div><div><dt>Клиент / служба</dt><dd>{device.clientVersion} / {device.serviceVersion}</dd></div><div><dt>Офлайн-доступ до</dt><dd>{formatDate(device.offlineLeaseExpiresAt)}</dd></div></dl>{canEdit && <div className="device-actions"><button className="ghost small" onClick={() => api(`/admin/devices/${device.id}/force-sync`, { method: "POST" }).then(load)}>Синхронизировать</button><button className="ghost small" onClick={() => setReassigning(device)}>Перепривязать</button>{!device.revoked && <button className="danger-link" onClick={() => emergency(device)}>Аварийный код</button>}<button className="danger-link" onClick={() => revoke(device)}>Отозвать</button></div>}</article>)}{devices.length === 0 && <Empty text="Зарегистрированных компьютеров пока нет" />}</div>
    {code && <Modal title={code.title} onClose={() => setCode(null)}><div className="code-box"><small>Действует до {formatDate(code.expiresAt)}</small><strong>{code.code}</strong><p>Покажите код только сотруднику за указанным компьютером. Повторное применение невозможно.</p></div></Modal>}
    {reassigning && <DeviceReassignModal device={reassigning} employees={employees.filter((employee) => !employee.device || employee.id === reassigning.employee.id)} onClose={() => setReassigning(null)} onSaved={() => { setReassigning(null); load(); }} />}
  </>;
}

function DeviceReassignModal({ device, employees, onClose, onSaved }: { device: Device; employees: Employee[]; onClose: () => void; onSaved: () => void }) {
  const [employeeId, setEmployeeId] = useState(device.employee.id); const [error, setError] = useState("");
  const submit = async (event: FormEvent) => { event.preventDefault(); try { await api(`/admin/devices/${device.id}/reassign`, { method: "POST", body: JSON.stringify({ employeeId }) }); onSaved(); } catch (e) { setError((e as Error).message); } };
  return <Modal title={`Перепривязать · ${device.name}`} onClose={onClose}><form className="stack-form" onSubmit={submit}><div className="warning-box">При следующей синхронизации локальные тесты, очередь и подтверждения прежнего сотрудника будут очищены.</div><label>Новый сотрудник<select required value={employeeId} onChange={(e) => setEmployeeId(e.target.value)}>{employees.map((employee) => <option key={employee.id} value={employee.id}>{employee.fullName} (@{employee.login})</option>)}</select></label>{error && <div className="form-error">{error}</div>}<button className="primary">Подтвердить перепривязку</button></form></Modal>;
}

function SettingsView() {
  const [accounts, setAccounts] = useState<AdminAccount[]>([]); const [open, setOpen] = useState(false); const [error, setError] = useState("");
  const load = useCallback(() => api<AdminAccount[]>("/admin/users").then(setAccounts).catch((e) => setError(e.message)), []); useEffect(() => { load(); }, [load]);
  return <><PageHeader eyebrow="Доступ к закрытой панели" title="Администраторы и роли" action={<button className="primary" onClick={() => setOpen(true)}>+ Добавить пользователя</button>} />{error && <ErrorBanner message={error} />}<div className="table-wrap"><table><thead><tr><th>Логин</th><th>Роль</th><th>Статус</th><th>Последний вход</th><th>Создан</th></tr></thead><tbody>{accounts.map((account) => <tr key={account.id}><td><b>@{account.login}</b></td><td>{account.role}</td><td><StatusPill status={account.active ? "Active" : "Disabled"} /></td><td>{formatDate(account.lastLoginAt)}</td><td>{formatDate(account.createdAt)}</td></tr>)}</tbody></table></div>{open && <AdminUserModal onClose={() => setOpen(false)} onSaved={() => { setOpen(false); load(); }} />}</>;
}

function AdminUserModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({ login: "", temporaryPassword: "", role: "Viewer" }); const [error, setError] = useState("");
  const submit = async (event: FormEvent) => { event.preventDefault(); try { await api("/admin/users", { method: "POST", body: JSON.stringify(form) }); onSaved(); } catch (e) { setError((e as Error).message); } };
  return <Modal title="Новый пользователь админки" onClose={onClose}><form className="stack-form" onSubmit={submit}><label>Логин<input required value={form.login} onChange={(e) => setForm({ ...form, login: e.target.value })} /></label><label>Временный пароль<input required type="password" minLength={8} value={form.temporaryPassword} onChange={(e) => setForm({ ...form, temporaryPassword: e.target.value })} /></label><label>Роль<select value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value })}><option value="Viewer">Viewer — только просмотр</option><option value="Admin">Admin — полное управление</option></select></label>{error && <div className="form-error">{error}</div>}<button className="primary">Создать пользователя</button></form></Modal>;
}

function AuditView() {
  const [items, setItems] = useState<Audit[]>([]); const [error, setError] = useState(""); useEffect(() => { api<Audit[]>("/admin/audit?take=200").then(setItems).catch((e) => setError(e.message)); }, []);
  return <><PageHeader eyebrow="Неизменяемая история действий" title="Журнал аудита" />{error && <ErrorBanner message={error} />}<div className="timeline">{items.map((item) => <article key={item.id}><span /><time>{formatDate(item.createdAt)}</time><div><b>{item.action}</b><p>{item.actor} · {item.entityType}{item.entityId ? ` · ${item.entityId.slice(0, 8)}` : ""}</p></div></article>)}{items.length === 0 && <Empty text="Событий аудита пока нет" />}</div></>;
}

function EmployeeDetailsModal({ details, onClose }: { details: EmployeeDetails; onClose: () => void }) {
  return <Modal title={details.employee.fullName} onClose={onClose}><div className="employee-details"><div className="detail-summary"><span className="avatar large">{initials(details.employee.fullName)}</span><div><b>@{details.employee.login}</b><p>{details.employee.position ? `${details.employee.position} · ` : ""}{details.employee.group ?? "Все сотрудники"} · {details.employee.device ?? "ПК не привязан"}</p></div><StatusPill status={details.employee.state} /></div><h3>История допуска</h3><div className="history-list detailed">{details.history.map((item) => <details key={item.id}><summary><time>{item.workday}</time><StatusPill status={item.status} /><span>{item.durationSeconds ? `${Math.round(item.durationSeconds / 60)} мин` : "—"}</span><small>{item.wasOffline == null ? "—" : item.wasOffline ? "Офлайн" : "Онлайн"}</small></summary><div className="history-body"><p><b>Начало:</b> {formatDate(item.startedAt)} · <b>завершение:</b> {formatDate(item.completedAt)}</p><p><b>Устройство:</b> {item.device ?? "—"} · client {item.clientVersion ?? "—"} · service {item.serviceVersion ?? "—"}</p><div className="answer-list">{item.answers?.map((answer) => <div key={answer.questionId}><b>{answer.question}</b><p className={answer.skipped ? "muted" : ""}>{answer.skipped ? "Ответ пропущен" : answer.selectedOptions.join(", ")}</p></div>)}</div></div></details>)}{details.history.length === 0 && <Empty text="История пока пуста" />}</div></div></Modal>;
}

function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: React.ReactNode }) {
  return <div className="modal-backdrop" role="presentation" onMouseDown={(e) => { if (e.currentTarget === e.target) onClose(); }}><section className="modal" role="dialog" aria-modal="true" aria-label={title}><header><h2>{title}</h2><button onClick={onClose} aria-label="Закрыть">×</button></header>{children}</section></div>;
}
function Empty({ text }: { text: string }) { return <div className="empty"><span>·</span><p>{text}</p></div>; }
function ErrorBanner({ message }: { message: string }) { return <div className="error-banner"><b>Не удалось получить данные.</b><span>{message}</span></div>; }
