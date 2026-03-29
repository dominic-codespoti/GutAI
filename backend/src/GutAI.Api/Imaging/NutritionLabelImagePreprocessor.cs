using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace GutAI.Api.Imaging;

public sealed record PreprocessedImage(
    MemoryStream Stream,
    string ContentType,
    long OutputBytes,
    long ElapsedMilliseconds) : IDisposable
{
    public void Dispose() => Stream.Dispose();
}

public static class NutritionLabelImagePreprocessor
{
    public const int MaxDimension = 2000;
    public const int JpegQuality = 80;

    public static async Task<PreprocessedImage> PreprocessAsync(Stream input, CancellationToken ct = default)
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
                    Size = new Size(MaxDimension, MaxDimension)
                });
            }

            // Mild normalization helps OCR on label photos without over-processing.
            x.Grayscale();
        });

        var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = JpegQuality }, ct);
        output.Position = 0;
        sw.Stop();

        return new PreprocessedImage(output, "image/jpeg", output.Length, sw.ElapsedMilliseconds);
    }
}
