using FluentAssertions;
using GutAI.Api.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace GutAI.Api.Tests;

public class NutritionLabelImagePreprocessorTests
{
    [Fact]
    public async Task PreprocessAsync_ReturnsJpegAndResizesLargeImage()
    {
        await using var input = new MemoryStream();
        using (var image = new Image<Rgba32>(2000, 1000))
        {
            image.Mutate(x => x.BackgroundColor(Color.White));
            await image.SaveAsPngAsync(input);
        }

        input.Position = 0;

        using var result = await NutritionLabelImagePreprocessor.PreprocessAsync(input);
        
        result.ContentType.Should().Be("image/jpeg");
        result.OutputBytes.Should().BeGreaterThan(0);
        result.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(0);
        result.Stream.Length.Should().BeGreaterThan(0);

        var bytes = result.Stream.ToArray();
        bytes[0].Should().Be(0xFF);
        bytes[1].Should().Be(0xD8);

        result.Stream.Position = 0;
        using var output = await Image.LoadAsync(result.Stream);
        output.Width.Should().Be(2000);
        output.Height.Should().Be(1000);
    }

    [Fact]
    public async Task PreprocessAsync_PreservesSmallerImageDimensions()
    {
        await using var input = new MemoryStream();
        using (var image = new Image<Rgba32>(800, 600))
        {
            image.Mutate(x => x.BackgroundColor(Color.White));
            await image.SaveAsPngAsync(input);
        }

        input.Position = 0;

        using var result = await NutritionLabelImagePreprocessor.PreprocessAsync(input);

        result.ContentType.Should().Be("image/jpeg");
        result.OutputBytes.Should().BeGreaterThan(0);
        result.Stream.Position = 0;

        using var output = await Image.LoadAsync(result.Stream);
        output.Width.Should().Be(800);
        output.Height.Should().Be(600);
    }
}
