using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

public interface IOfflineFoodDatabase
{
    Task<FoodProductDto?> LookupByBarcodeAsync(string barcode, CancellationToken ct = default);
}
