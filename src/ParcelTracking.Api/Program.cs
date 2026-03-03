using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ParcelTracking.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure (EF Core, Redis, Service Bus) ──────────────
builder.Services.AddInfrastructure(builder.Configuration);

// ── Controllers ───────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        opts.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// ── ProblemDetails (RFC 7807) — prevents stack traces leaking ─
// OWASP A05: Security Misconfiguration
builder.Services.AddProblemDetails();

// ── Swagger ───────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Parcel Tracking API", Version = "v1" });
});

// ── Rate Limiting — OWASP A07: prevents brute-force/enumeration
// Fixed window: 100 req / 1 min per IP; 429 on breach
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("api", limiterOptions =>
    {
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.PermitLimit = 100;
        limiterOptions.QueueLimit = 0;
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

// ── CORS — explicit policy; tighten Origins for production ────
// OWASP A05: replaces implicit allow-all
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSwaggerAndLocalClients", policy =>
        policy.WithOrigins(
                "http://localhost:5058",
                "http://localhost:8080")   // Adminer + Swagger UI origin
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// ── Health checks ─────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("SqlServer")!,
        name: "sqlserver",
        tags: ["ready"])
    .AddRedis(
        builder.Configuration.GetConnectionString("Redis")!,
        name: "redis",
        tags: ["ready"]);

var app = builder.Build();

// ── Global exception handler — OWASP A05 ─────────────────────
// Returns RFC 7807 ProblemDetails JSON; never leaks stack traces
app.UseExceptionHandler(errApp =>
{
    errApp.Run(async ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            type   = "https://tools.ietf.org/html/rfc7807",
            title  = "An unexpected error occurred.",
            status = 500,
            traceId = ctx.TraceIdentifier   // correlate with logs; no internals exposed
        });
    });
});

// ── Security headers — OWASP A05 ─────────────────────────────
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"]  = "nosniff";
    ctx.Response.Headers["X-Frame-Options"]         = "SAMEORIGIN";
    ctx.Response.Headers["X-XSS-Protection"]        = "1; mode=block";
    ctx.Response.Headers["Referrer-Policy"]          = "no-referrer";
    ctx.Response.Headers["Permissions-Policy"]       = "geolocation=(), microphone=(), camera=()";
    // Allow Swagger UI inline scripts/styles (demo); tighten for production
    ctx.Response.Headers["Content-Security-Policy"]  =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:";
    await next();
});

// ── Swagger (all environments for demo) ───────────────────────
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Parcel Tracking API v1");
    c.RoutePrefix = "swagger";
});

// ── Health check endpoints ────────────────────────────────────
// /healthz/live  — liveness  (process is up)
// /healthz/ready — readiness (dependencies reachable)
app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = _ => false,   // liveness: no dependency checks
    ResultStatusCodes =
    {
        [HealthStatus.Healthy]   = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResultStatusCodes =
    {
        [HealthStatus.Healthy]   = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.UseCors("AllowSwaggerAndLocalClients");
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers().RequireRateLimiting("api");

app.Run();

