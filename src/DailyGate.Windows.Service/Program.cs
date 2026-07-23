using DailyGate.Windows.Service;

if (args.Length > 0 && args[0].Equals("enroll", StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode = await EnrollmentCommand.RunAsync(args[1..]);
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "DailyGate Service");
builder.Services.AddSingleton<LocalProtector>();
builder.Services.AddSingleton<DeviceCredentialStore>();
builder.Services.AddSingleton<LocalRepository>();
builder.Services.AddSingleton<SignatureVerifier>();
builder.Services.AddSingleton<OfflinePasswordVerifier>();
builder.Services.AddSingleton<ClientSession>();
builder.Services.AddSingleton<ClientCommandHandler>();
builder.Services.AddSingleton<WindowsSessionController>();
builder.Services.AddHttpClient<DeviceApiClient>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.All
});
builder.Services.AddHostedService<PipeServer>();
builder.Services.AddHostedService<DailyGateWorker>();
builder.Services.Configure<HostOptions>(options =>
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

await builder.Build().RunAsync();
