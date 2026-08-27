using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace GutAI.Api.Imaging;

/// <summary>A color-preserving preprocessed meal photo ready for vision inference.</summary>
public sealed record PreprocessedMealPhoto(
    MemoryStream Stream,
    string ContentType,
    long OutputBytes,
    long ElapsedMilliseconds) : IDisposable
{
    public void Dispose() => Stream.Dispose();
}

/// <summary>
/// Meal-photo preprocessing for vision inference. Preserves color: hue carries
/// load-bearing food-identity evidence (red sauce vs meat, green vegetables,
/// yellow oats/eggs, orange peanut butter, pink fish). Do NOT reuse the
/// grayscale label-OCR preprocessor here.
/// </summary>
public static class MealPhotoPreprocessor
{
    public const int MaxDimension = 2000;
    public const int JpegQuality = 85;

    public static async Task<PreprocessedMealPhoto> PreprocessAsync(Stream input, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        using var image = await Image.LoadAsync(input, ct);

        image.Mutate(x =>
        {
            x.AutoOrient();

            if (image.Width > MaxDimension || image.Height > MaxDimension)
            {
                x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(MaxDimension, MaxDimension),
                });
            }
        });

        var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = JpegQuality }, ct);
        output.Position = 0;
        sw.Stop();

        return new PreprocessedMealPhoto(output, "image/jpeg", output.Length, sw.ElapsedMilliseconds);
    }
}
