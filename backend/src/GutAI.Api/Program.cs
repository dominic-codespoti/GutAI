using System.Text;
using System.IdentityModel.Tokens.Jwt;
using GutAI.Api.Middleware;
using GutAI.Application.Common.Interfaces;
using GutAI.Infrastructure;
using GutAI.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI
builder.Services.AddOpenApi();

// Infrastructure (Table Storage, cache, HTTP clients)
builder.Services.AddInfrastructure(builder.Configuration);

// Rate limiting
builder.Services.AddGutAIRateLimiting();

// Auth
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret must be configured. Set Jwt__Secret environment variable.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "GutAI",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "GutAI",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();

// CORS
builder.Services.AddCors(options =>
{
    if (builder.Environment.IsDevelopment())
    {
        options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
    }
    else
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        options.AddDefaultPolicy(policy =>
            policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials());
    }
});

// Health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Middleware pipeline
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

// Seed in development
if (app.Environment.IsDevelopment())
{
    var store = app.Services.GetRequiredService<ITableStore>();
    await DbSeeder.SeedAsync(store);
}

app.Run();

public partial class Program { }
