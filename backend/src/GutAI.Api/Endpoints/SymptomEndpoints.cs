using System.Security.Claims;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Interfaces;
using GutAI.Domain.Entities;

public static class SymptomEndpoints
{
    public static RouteGroupBuilder MapSymptomEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", LogSymptom);
        group.MapGet("/", GetSymptomsByDate);
        group.MapGet("/history", GetSymptomHistory);
        group.MapGet("/types", GetSymptomTypes);
        group.MapGet("/{id:guid}", GetSymptom);
        group.MapPut("/{id:guid}", UpdateSymptom);
        group.MapDelete("/{id:guid}", DeleteSymptom);
        return group;
    }

    static Guid GetUserId(ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("sub")!);

    static async Task<IResult> LogSymptom(CreateSymptomRequest request, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);

        if (request.Severity < 1 || request.Severity > 10)
            return Results.BadRequest(new { error = "Severity must be between 1 and 10" });

        if (request.Notes is not null && request.Notes.Length > 1000)
            return Results.BadRequest(new { error = "Notes must not exceed 1000 characters" });

        if (request.Duration.HasValue && (request.Duration.Value < TimeSpan.Zero || request.Duration.Value > TimeSpan.FromDays(7)))
            return Results.BadRequest(new { error = "Duration must be between 0 and 7 days" });

        if (!await store.SymptomTypeExistsAsync(request.SymptomTypeId))
            return Results.BadRequest(new { error = "Invalid symptom type" });

        if (request.RelatedMealLogId.HasValue && await store.GetMealLogAsync(userId, request.RelatedMealLogId.Value) is null)
            return Results.BadRequest(new { error = "Meal not found" });

        var symptom = new SymptomLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SymptomTypeId = request.SymptomTypeId,
            Severity = request.Severity,
            OccurredAt = request.OccurredAt ?? DateTime.UtcNow,
            Notes = request.Notes,
            RelatedMealLogId = request.RelatedMealLogId,
            Duration = request.Duration
        };

        await store.UpsertSymptomLogAsync(symptom);
        return Results.Created($"/api/symptoms/{symptom.Id}", MapToDto(symptom));
    }

    static async Task<IResult> GetSymptomsByDate(DateOnly? date, int? tzOffsetMinutes, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // When a timezone offset is provided, shift the UTC boundaries so
        // "2026-03-07" means midnight-to-midnight in the user's local time.
        // JS getTimezoneOffset() returns minutes *behind* UTC (e.g. UTC+10 → -600).
        if (tzOffsetMinutes.HasValue)
        {
            var offset = TimeSpan.FromMinutes(-tzOffsetMinutes.Value);
            var localStart = targetDate.ToDateTime(TimeOnly.MinValue);
            var localEnd = targetDate.ToDateTime(TimeOnly.MaxValue);
            var utcStart = localStart - offset;
            var utcEnd = localEnd - offset;
            var symptoms = await store.GetSymptomLogsByDateRangeAsync(
                userId,
                DateOnly.FromDateTime(utcStart),
                DateOnly.FromDateTime(utcEnd),
                default);
            // Further filter to exact UTC boundaries
            symptoms = symptoms.Where(s => s.OccurredAt >= utcStart && s.OccurredAt <= utcEnd).ToList();
            foreach (var s in symptoms)
                s.SymptomType = await store.GetSymptomTypeAsync(s.SymptomTypeId);
            return Results.Ok(symptoms.OrderBy(s => s.OccurredAt).Select(MapToDto));
        }

        var targetSymptoms = await store.GetSymptomLogsByDateAsync(userId, targetDate);
        foreach (var s in targetSymptoms)
            s.SymptomType = await store.GetSymptomTypeAsync(s.SymptomTypeId);
        return Results.Ok(targetSymptoms.OrderBy(s => s.OccurredAt).Select(MapToDto));
    }

    static async Task<IResult> GetSymptomHistory(DateOnly? from, DateOnly? to, int? typeId, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var symptoms = await store.GetSymptomLogsByDateRangeAsync(userId, fromDate, toDate);
        if (typeId.HasValue)
            symptoms = symptoms.Where(s => s.SymptomTypeId == typeId.Value).ToList();
        foreach (var s in symptoms)
            s.SymptomType = await store.GetSymptomTypeAsync(s.SymptomTypeId);
        return Results.Ok(symptoms.OrderBy(s => s.OccurredAt).Select(MapToDto));
    }

    static async Task<IResult> GetSymptomTypes(ITableStore store)
    {
        var types = await store.GetAllSymptomTypesAsync();
        return Results.Ok(types.OrderBy(t => t.Name).Select(t => new
        {
            id = t.Id,
            name = t.Name,
            category = t.Category,
            icon = t.Icon
        }));
    }

    static async Task<IResult> GetSymptom(Guid id, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var symptom = await store.GetSymptomLogAsync(userId, id);
        if (symptom is null) return Results.NotFound();
        symptom.SymptomType = await store.GetSymptomTypeAsync(symptom.SymptomTypeId);
        return Results.Ok(MapToDto(symptom));
    }

    static async Task<IResult> UpdateSymptom(Guid id, CreateSymptomRequest request, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var symptom = await store.GetSymptomLogAsync(userId, id);
        if (symptom is null) return Results.NotFound();

        if (request.Severity < 1 || request.Severity > 10)
            return Results.BadRequest(new { error = "Severity must be between 1 and 10" });

        if (request.Notes is not null && request.Notes.Length > 1000)
            return Results.BadRequest(new { error = "Notes must not exceed 1000 characters" });

        if (request.Duration.HasValue && (request.Duration.Value < TimeSpan.Zero || request.Duration.Value > TimeSpan.FromDays(7)))
            return Results.BadRequest(new { error = "Duration must be between 0 and 7 days" });

        if (!await store.SymptomTypeExistsAsync(request.SymptomTypeId))
            return Results.BadRequest(new { error = "Invalid symptom type" });

        if (request.RelatedMealLogId.HasValue && await store.GetMealLogAsync(userId, request.RelatedMealLogId.Value) is null)
            return Results.BadRequest(new { error = "Meal not found" });

        symptom.SymptomTypeId = request.SymptomTypeId;
        symptom.Severity = request.Severity;
        symptom.Notes = request.Notes;
        symptom.RelatedMealLogId = request.RelatedMealLogId;
        symptom.Duration = request.Duration;
        if (request.OccurredAt.HasValue)
            symptom.OccurredAt = request.OccurredAt.Value;
        await store.UpsertSymptomLogAsync(symptom);
        symptom.SymptomType = await store.GetSymptomTypeAsync(symptom.SymptomTypeId);
        return Results.Ok(MapToDto(symptom));
    }

    static async Task<IResult> DeleteSymptom(Guid id, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var symptom = await store.GetSymptomLogAsync(userId, id);
        if (symptom is null) return Results.NotFound();
        symptom.IsDeleted = true;
        await store.UpsertSymptomLogAsync(symptom);
        return Results.NoContent();
    }

    static SymptomLogDto MapToDto(SymptomLog s) => new()
    {
        Id = s.Id,
        SymptomTypeId = s.SymptomTypeId,
        SymptomName = s.SymptomType?.Name ?? "Unknown",
        Category = s.SymptomType?.Category ?? "Other",
        Icon = s.SymptomType?.Icon ?? "🩺",
        Severity = s.Severity,
        OccurredAt = s.OccurredAt,
        Notes = s.Notes,
        RelatedMealLogId = s.RelatedMealLogId,
        Duration = s.Duration
    };
}
