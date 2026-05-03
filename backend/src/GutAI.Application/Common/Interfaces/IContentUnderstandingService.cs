using System.IO;
using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

public interface IContentUnderstandingService
{
    Task<CustomFoodDto?> ParseNutritionLabelAsync(Stream imageStream, string contentType, CancellationToken ct = default);
    Task<CustomFoodDto?> DescribeFoodFromTextAsync(string description, CancellationToken ct = default);
}
