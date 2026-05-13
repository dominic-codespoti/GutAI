using System.Text;
using System.IdentityModel.Tokens.Jwt;
using GutAI.Api.Middleware;
using GutAI.Application.Common;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure;
using GutAI.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using System.Text.Json;
using Microsoft.AspNetCore.ResponseCompression;
using ModelContextProtocol.AspNetCore;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI
builder.Services.AddOpenApi();

// Infrastructure (Table Storage, cache, HTTP clients)
builder.Services.AddInfrastructure(builder.Configuration);

// Rate limiting
builder.Services.AddGutAIRateLimiting();

// Bind JwtSettings from config + env vars
var jwtSection = builder.Configuration.GetSection(JwtSettings.SectionName);
builder.Services.Configure<JwtSettings>(jwtSection);

var jwtSettings = jwtSection.Get<JwtSettings>() ?? new JwtSettings();
if (string.IsNullOrWhiteSpace(jwtSettings.Secret) || jwtSettings.Secret.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Secret must be configured and at least 32 characters. Set the Jwt__Secret environment variable.");

// Auth
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();

// CORS — mobile apps don't send Origin headers so CORS is a no-op there.
// Enabled unconditionally so the Expo web dev server can reach the API.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// Response compression for smaller payloads over the wire
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    options.Level = System.IO.Compression.CompressionLevel.Fastest);

// MCP server (exposes gut health tools to external AI apps)
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

// Health checks
builder.Services.AddHealthChecks();

// Application Insights (only when configured; local dev should boot without it)
var appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

var app = builder.Build();

// Middleware pipeline
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/health")
    {
        var activity = System.Diagnostics.Activity.Current;
        if (activity != null)
        {
            activity.ActivityTraceFlags &= ~System.Diagnostics.ActivityTraceFlags.Recorded;
            activity.IsAllDataRequested = false;
        }
    }
    await next();
});
app.UseResponseCompression();
app.UseMiddleware<ExceptionMiddleware>();
app.UseCors();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseAuthorization();

// Health check
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration.TotalMilliseconds
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
});

// API Endpoints
app.MapGroup("/api/auth").MapAuthEndpoints().RequireRateLimiting("auth");
app.MapGroup("/api/meals").MapMealEndpoints().RequireAuthorization().RequireRateLimiting("authenticated");
app.MapGroup("/api/food").MapFoodEndpoints().RequireAuthorization().RequireRateLimiting("search");
app.MapGroup("/api/symptoms").MapSymptomEndpoints().RequireAuthorization().RequireRateLimiting("authenticated");
app.MapGroup("/api/insights").MapInsightEndpoints().RequireAuthorization().RequireRateLimiting("authenticated");
app.MapGroup("/api/user").MapUserEndpoints().RequireAuthorization().RequireRateLimiting("authenticated");
app.MapGroup("/api/chat").MapChatEndpoints().RequireAuthorization().RequireRateLimiting("chat");

// MCP endpoint for external AI apps
app.MapMcp().RequireAuthorization();

// ── CLI commands ──────────────────────────────────────────────────────────────
if (args.Contains("--import-off"))
{
    await RunOffImportAsync(app.Services);
    return;
}

static async Task RunOffImportAsync(IServiceProvider services)
{
    var logger = services.GetRequiredService<ILogger<Program>>();
    var db = services.GetRequiredService<IOfflineFoodDatabase>();
    if (db is not AzureTableOfflineDatabase offlineDb)
    {
        logger.LogError("Offline database is not an AzureTableOfflineDatabase — cannot import");
        return;
    }

    logger.LogInformation("Starting OFF data dump import...");

    var offlineDumpPath = "/home/dom/openfoodfacts-products.jsonl.gz";
    Stream stream;

    var progress = new Progress<int>(count =>
    {
        if (count % 10000 == 0)
            logger.LogInformation("Imported {Count} products...", count);
    });

    if (File.Exists(offlineDumpPath))
    {
        logger.LogInformation("Using local file: {Path}", offlineDumpPath);
        stream = File.OpenRead(offlineDumpPath);
    }
    else
    {
        logger.LogInformation("Downloading from https://static.openfoodfacts.org/data/openfoodfacts-products.jsonl.gz");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.Add("User-Agent", "GutAI/1.0 (contact@gutai.app)");

        var response = await http.GetAsync("https://static.openfoodfacts.org/data/openfoodfacts-products.jsonl.gz",
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        stream = await response.Content.ReadAsStreamAsync();
    }

    await using (stream)
    {
        await offlineDb.ImportFromOffDumpAsync(stream, progress);
    }

    logger.LogInformation("OFF data dump import complete.");
}

// Seed reference data (symptom types, food additives) asynchronously
_ = Task.Run(async () =>
{
    try
    {
        using var scope = app.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITableStore>();
        await DbSeeder.SeedAsync(store);
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Reference data seeding failed");
    }
});

app.Run();

public partial class Program { }
