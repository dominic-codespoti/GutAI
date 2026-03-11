using GutAI.Application.Common.DTOs;

namespace GutAI.Application.Common.Interfaces;

public interface IGlycemicIndexService
{
    GlycemicAssessmentDto Assess(FoodProductDto product);
    GlycemicAssessmentDto AssessText(string text);
}
