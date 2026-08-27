using System.Security.Claims;
using GutAI.Application.Common.DTOs;
using GutAI.Application.Common.Helpers;
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
            OccurredAt = request.OccurredAt.HasValue
                ? TimeZoneHelper.NormalizeUtc(request.OccurredAt.Value)
                : DateTime.UtcNow,
            Notes = request.Notes,
            RelatedMealLogId = request.RelatedMealLogId,
            Duration = request.Duration
        };

        await store.UpsertSymptomLogAsync(symptom);
        symptom.SymptomType = await store.GetSymptomTypeAsync(symptom.SymptomTypeId);
        return Results.Created($"/api/symptoms/{symptom.Id}", MapToDto(symptom));
    }

    static async Task<IResult> GetSymptomsByDate(
        DateOnly? date,
        int? tzOffsetMinutes,
        string? timezoneId,
        ClaimsPrincipal principal,
        ITableStore store)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);
        var hasTimezone = !string.IsNullOrWhiteSpace(timezoneId)
            || !string.IsNullOrWhiteSpace(user?.TimezoneId);
        var timezone = TimeZoneHelper.ResolveTimeZone(user, timezoneId);
        var targetDate = date
            ?? (hasTimezone
                ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone))
                : tzOffsetMinutes.HasValue
                    ? DateOnly.FromDateTime(DateTime.UtcNow.Add(TimeSpan.FromMinutes(-tzOffsetMinutes.Value)))
                    : DateOnly.FromDateTime(DateTime.UtcNow));
        var (utcStart, utcEnd) = hasTimezone
            ? TimeZoneHelper.GetUtcRangeForLocalDate(user, targetDate, timezoneId)
            : tzOffsetMinutes.HasValue
                ? TimeZoneHelper.GetUtcRangeForFixedOffset(targetDate, tzOffsetMinutes.Value)
                : TimeZoneHelper.GetUtcRangeForLocalDate(user, targetDate);

        var symptoms = await store.GetSymptomLogsByDateRangeAsync(
            userId,
            DateOnly.FromDateTime(utcStart),
            DateOnly.FromDateTime(utcEnd));
        symptoms = symptoms.Where(s => s.OccurredAt >= utcStart && s.OccurredAt <= utcEnd).ToList();
        foreach (var s in symptoms)
            s.SymptomType = await store.GetSymptomTypeAsync(s.SymptomTypeId);
        return Results.Ok(symptoms.OrderBy(s => s.OccurredAt).Select(MapToDto));
    }

    static async Task<IResult> GetSymptomHistory(DateOnly? from, DateOnly? to, int? typeId, string? timezoneId, ClaimsPrincipal principal, ITableStore store)
    {
        var userId = GetUserId(principal);
        var user = await store.GetUserAsync(userId);
        var tz = TimeZoneHelper.ResolveTimeZone(user, timezoneId);
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        var fromDate = from ?? todayLocal.AddDays(-30);
        var toDate = to ?? todayLocal;

        var (utcStart, utcEnd) = TimeZoneHelper.GetUtcRangeForLocalDateRange(user, fromDate, toDate, timezoneId);
        var symptoms = await store.GetSymptomLogsByDateRangeAsync(userId, DateOnly.FromDateTime(utcStart), DateOnly.FromDateTime(utcEnd));
        symptoms = symptoms.Where(s => s.OccurredAt >= utcStart && s.OccurredAt <= utcEnd).ToList();
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
            symptom.OccurredAt = TimeZoneHelper.NormalizeUtc(request.OccurredAt.Value);
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
