using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DailyGate.Api.Auth;
using DailyGate.Api.Data;
using DailyGate.Api.Endpoints;
using DailyGate.Api.Infrastructure;
using DailyGate.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<DailyGateOptions>(builder.Configuration.GetSection(DailyGateOptions.Section));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
builder.Services.AddDbContext<DailyGateDbContext>(options => options
    .UseNpgsql(connectionString, provider =>
        provider.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<ServerSigningService>();
builder.Services.AddScoped<WorkdayService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<DailyTestProvisioner>();
var dailyGateSettings = builder.Configuration.GetSection(DailyGateOptions.Section).Get<DailyGateOptions>() ?? new DailyGateOptions();
if (dailyGateSettings.RunBootstrap) builder.Services.AddHostedService<BootstrapService>();
if (dailyGateSettings.RunWorker) builder.Services.AddHostedService<DailyTestWorker>();

var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration is required.");
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue("dailygate_admin", out var token)) context.Token = token;
                return Task.CompletedTask;
            }
        };
    })
    .AddScheme<AuthenticationSchemeOptions, DeviceSignatureAuthenticationHandler>(
        DeviceSignatureAuthenticationHandler.Scheme, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(DeviceSignatureAuthenticationHandler.Scheme, policy =>
        policy.AddAuthenticationSchemes(DeviceSignatureAuthenticationHandler.Scheme).RequireAuthenticatedUser());
    options.AddPolicy("Viewer", policy => policy.RequireAuthenticatedUser().RequireRole("Admin", "Viewer"));
    options.AddPolicy("AdminOnly", policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));
});

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length > 0) policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "DailyGate API", Version = "v1" });
    options.AddSecurityDefinition("cookieAuth", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Cookie, Name = "dailygate_admin"
    });
});

var app = builder.Build();
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
// The API has no published host port in Compose and only Caddy can reach it on the edge network.
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);
app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/api/v1", () => Results.Ok(new
{
    service = "DailyGate API",
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "dev",
    serverTime = DateTimeOffset.UtcNow
}));
app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapEmployeeEndpoints();
app.MapDeviceEndpoints();
app.MapTestManagementEndpoints();
app.MapAnalyticsEndpoints();

app.Run();

public partial class Program { }
