using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DailyGate.Shared;

var baseUrl = args.ElementAtOrDefault(0) ?? "http://127.0.0.1:8088";
var adminLogin = args.ElementAtOrDefault(1) ?? "admin";
var adminPassword = args.ElementAtOrDefault(2) ?? "ChangeMe123!";
var suffix = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

var cookies = new CookieContainer();
using var http = new HttpClient(new HttpClientHandler { CookieContainer = cookies }) { BaseAddress = new Uri(baseUrl) };
await PostAdmin("/api/v1/admin/auth/login", new { login = adminLogin, password = adminPassword });

var groupId = await Id(PostAdmin("/api/v1/admin/groups", new { name = $"Probe {suffix}" }));
var employeeLogin = $"probe{suffix}";
var employeePassword = "EmployeePass1!";
var employeeId = await Id(PostAdmin("/api/v1/admin/employees", new { fullName = "Probe Employee", login = employeeLogin, temporaryPassword = employeePassword, groupId }));
var bankId = await Id(PostAdmin("/api/v1/admin/test-banks", new { name = $"Probe Bank {suffix}", description = "Automated end-to-end probe" }));
await PostAdmin($"/api/v1/admin/test-banks/{bankId}/questions", new { text = "Состояние рабочего места проверено?", type = "SingleChoice", options = new[] { "Да", "Нет" } });
await PostAdmin($"/api/v1/admin/test-banks/{bankId}/questions", new { text = "Какие проверки выполнены?", type = "MultipleChoice", options = new[] { "Оборудование", "Документы", "Безопасность" } });
await PostAdmin($"/api/v1/admin/test-banks/{bankId}/publish", new { });
await PostAdmin("/api/v1/admin/test-rules", new { name = "Probe daily rule", questionBankId = bankId, employeeGroupId = groupId, questionCount = 2, timeLimitMinutes = 15, effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd") });
var enrollmentDoc = await PostAdmin("/api/v1/admin/devices/enrollment-codes", new { employeeId });
var enrollmentCode = enrollmentDoc.RootElement.GetProperty("code").GetString()!;

using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var enrollment = await PostAnonymous<DeviceEnrollmentResponse>("/api/v1/device/enroll", new DeviceEnrollmentRequest(
    enrollmentCode, "Probe-PC", $"probe-{suffix}", Convert.ToBase64String(deviceKey.ExportSubjectPublicKeyInfo()), "probe/1.0"));
var sync = await SendDevice<DeviceSyncResponse>(HttpMethod.Get, "/api/v1/device/sync", null, enrollment.DeviceId, deviceKey);
if (sync.Tests.Count == 0) throw new InvalidOperationException("Sync did not return a daily test.");
var login = await SendDevice<EmployeeLoginResponse>(HttpMethod.Post, "/api/v1/device/employee/login",
    new EmployeeLoginRequest(employeeLogin, employeePassword), enrollment.DeviceId, deviceKey);
if (login.EmployeeId != employeeId) throw new InvalidOperationException("Employee login returned the wrong identity.");

using (var progress = JsonDocument.Parse(await http.GetStringAsync($"/api/v1/admin/analytics/employees/{employeeId}")))
{
    var current = progress.RootElement.GetProperty("history").EnumerateArray()
        .Single(x => x.GetProperty("workday").GetString() == sync.Tests[0].Workday.ToString("yyyy-MM-dd"));
    if (current.GetProperty("status").GetString() != "InProgress")
        throw new InvalidOperationException("Employee login did not move the daily test to InProgress.");
}

var test = sync.Tests[0];
using var serverKey = ECDsa.Create();
serverKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String(sync.ServerPublicKey), out _);
if (!serverKey.VerifyData(Encoding.UTF8.GetBytes(test.PayloadJson), Convert.FromBase64String(test.Signature), HashAlgorithmName.SHA256))
    throw new InvalidOperationException("Daily test signature verification failed.");
var payload = test.Payload();
var now = DateTimeOffset.UtcNow;
var submission = new SubmissionRequest(Guid.NewGuid(), test.InstanceId, Guid.NewGuid().ToString("N"), SubmissionKind.Completed,
    now, now.AddSeconds(4), false, payload.Questions.Select(question => new SubmissionAnswerRequest(question.Id, new[] { question.Options[0].Id })).ToArray());
var receipt = await SendDevice<CompletionReceipt>(HttpMethod.Post, "/api/v1/device/submissions", submission, enrollment.DeviceId, deviceKey);
if (!serverKey.VerifyData(Encoding.UTF8.GetBytes(receipt.ReceiptJson), Convert.FromBase64String(receipt.Signature), HashAlgorithmName.SHA256))
    throw new InvalidOperationException("Completion receipt signature verification failed.");

var replay = await SendDevice<CompletionReceipt>(HttpMethod.Post, "/api/v1/device/submissions", submission, enrollment.DeviceId, deviceKey);
if (replay.SubmissionId != receipt.SubmissionId) throw new InvalidOperationException("Idempotent replay returned a different submission.");
var dashboard = await http.GetAsync("/api/v1/admin/analytics/dashboard");
dashboard.EnsureSuccessStatusCode();
using (var details = JsonDocument.Parse(await http.GetStringAsync($"/api/v1/admin/analytics/employees/{employeeId}")))
{
    var completed = details.RootElement.GetProperty("history").EnumerateArray()
        .Single(x => x.GetProperty("workday").GetString() == receipt.Workday.ToString("yyyy-MM-dd"));
    if (completed.GetProperty("device").GetString() != "Probe-PC"
        || !completed.GetProperty("answers")[0].GetProperty("selectedOptions").EnumerateArray().Any())
        throw new InvalidOperationException("Detailed analytics did not preserve the device snapshot and selected answers.");
}
var results = await http.GetAsync($"/api/v1/admin/analytics/results?employeeId={employeeId}&status=Completed");
results.EnsureSuccessStatusCode();
if (!JsonDocument.Parse(await results.Content.ReadAsStringAsync()).RootElement.EnumerateArray().Any())
    throw new InvalidOperationException("Filtered result analytics returned no completed row.");
(await http.GetAsync($"/api/v1/admin/analytics/export.csv?employeeId={employeeId}&status=Completed")).EnsureSuccessStatusCode();
(await http.GetAsync($"/api/v1/admin/analytics/export.xlsx?employeeId={employeeId}&status=Completed")).EnsureSuccessStatusCode();

var replacementId = await Id(PostAdmin("/api/v1/admin/employees", new
{
    fullName = "Replacement Employee", login = $"replacement{suffix}", temporaryPassword = "ReplacementPass1!", groupId, position = "Operator"
}));
await SendAdmin(HttpMethod.Post, $"/api/v1/admin/devices/{enrollment.DeviceId}/reassign", new { employeeId = replacementId });
var reassignedSync = await SendDevice<DeviceSyncResponse>(HttpMethod.Get, "/api/v1/device/sync", null, enrollment.DeviceId, deviceKey);
if (reassignedSync.EmployeeId != replacementId || reassignedSync.EmployeeLogin != $"replacement{suffix}")
    throw new InvalidOperationException("Device reassignment was not returned by synchronization.");
await SendAdmin(HttpMethod.Put, $"/api/v1/admin/employees/{replacementId}", new { fullName = "Replacement Employee", groupId, state = "Disabled", position = "Operator" });
var disabledSync = await SendDevice<DeviceSyncResponse>(HttpMethod.Get, "/api/v1/device/sync", null, enrollment.DeviceId, deviceKey);
if (disabledSync.EmployeeActive || disabledSync.Tests.Count != 0)
    throw new InvalidOperationException("A deactivated employee still received offline access or tests.");

await PostAdmin("/api/v1/admin/users", new
{
    login = $"viewer{suffix}", temporaryPassword = "ViewerPassword1!", role = "Viewer"
});
var viewerCookies = new CookieContainer();
using var viewerHttp = new HttpClient(new HttpClientHandler { CookieContainer = viewerCookies }) { BaseAddress = new Uri(baseUrl) };
var viewerLogin = await viewerHttp.PostAsJsonAsync("/api/v1/admin/auth/login", new
{
    login = $"viewer{suffix}", password = "ViewerPassword1!"
}, JsonDefaults.Options);
viewerLogin.EnsureSuccessStatusCode();
await Expect(viewerHttp, HttpMethod.Get, "/api/v1/admin/employees", HttpStatusCode.OK);
await Expect(viewerHttp, HttpMethod.Get, "/api/v1/admin/devices", HttpStatusCode.OK);
await Expect(viewerHttp, HttpMethod.Get, "/api/v1/admin/analytics/dashboard", HttpStatusCode.OK);
await Expect(viewerHttp, HttpMethod.Get, "/api/v1/admin/test-banks", HttpStatusCode.Forbidden);
await Expect(viewerHttp, HttpMethod.Post, "/api/v1/admin/employees", HttpStatusCode.Forbidden,
    new { fullName = "Forbidden", login = $"forbidden{suffix}", temporaryPassword = "ForbiddenPass1!", groupId });
Console.WriteLine($"DailyGate probe passed: employee={employeeId}, device={enrollment.DeviceId}, workday={receipt.Workday}, status={receipt.Status}");

static async Task Expect(HttpClient client, HttpMethod method, string path, HttpStatusCode expected, object? body = null)
{
    using var request = new HttpRequestMessage(method, path);
    if (body is not null)
        request.Content = JsonContent.Create(body, options: JsonDefaults.Options);
    using var response = await client.SendAsync(request);
    if (response.StatusCode != expected)
        throw new InvalidOperationException($"{method} {path}: expected {(int)expected}, got {(int)response.StatusCode}.");
}

async Task<JsonDocument> PostAdmin(string path, object body)
{
    var response = await http.PostAsJsonAsync(path, body, JsonDefaults.Options);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"POST {path} failed: {(int)response.StatusCode} {text}");
    return string.IsNullOrWhiteSpace(text) ? JsonDocument.Parse("{}") : JsonDocument.Parse(text);
}

async Task SendAdmin(HttpMethod method, string path, object body)
{
    using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, options: JsonDefaults.Options) };
    using var response = await http.SendAsync(request);
    var responseText = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException($"{method} {path} failed: {(int)response.StatusCode} {responseText}");
}

async Task<T> PostAnonymous<T>(string path, object body)
{
    var response = await http.PostAsJsonAsync(path, body, JsonDefaults.Options);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"POST {path} failed: {(int)response.StatusCode} {text}");
    return JsonSerializer.Deserialize<T>(text, JsonDefaults.Options)!;
}

async Task<T> SendDevice<T>(HttpMethod method, string path, object? body, Guid deviceId, ECDsa key)
{
    var json = body is null ? "" : JsonSerializer.Serialize(body, JsonDefaults.Options);
    var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
    var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    var canonical = $"{method.Method}\n{path}\n{unix}\n{nonce}\n{hash}";
    using var request = new HttpRequestMessage(method, path);
    if (body is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
    request.Headers.Add("X-Device-Id", deviceId.ToString()); request.Headers.Add("X-Device-Timestamp", unix.ToString());
    request.Headers.Add("X-Device-Nonce", nonce); request.Headers.Add("X-Device-Signature", Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256)));
    using var response = await http.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"DEVICE {method} {path} failed: {(int)response.StatusCode} {text}");
    return JsonSerializer.Deserialize<T>(text, JsonDefaults.Options)!;
}

static async Task<Guid> Id(Task<JsonDocument> pending)
{
    using var document = await pending;
    return document.RootElement.GetProperty("id").GetGuid();
}
