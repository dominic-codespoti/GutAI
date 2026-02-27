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

// Health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Middleware pipeline
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
