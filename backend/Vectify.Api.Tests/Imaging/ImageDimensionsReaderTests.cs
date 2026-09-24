using Vectify.Api.Imaging;
using Vectify.Api.Tests.TestSupport;

namespace Vectify.Api.Tests.Imaging;

/// <summary>
/// Pruebas unitarias de ImageDimensionsReader: lectura best-effort de width/height
/// (spec.md: "cuando estén disponibles"), incluyendo el caso en que no se puede leer.
/// </summary>
public sealed class ImageDimensionsReaderTests
{
    [Fact]
    public void TryRead_WhenPng_ReturnsWidthAndHeight()
    {
        using var stream = new MemoryStream(SampleImages.ValidPng1x1);

        var ok = ImageDimensionsReader.TryRead(stream, "image/png", out var width, out var height);

        Assert.True(ok);
        Assert.Equal(1, width);
        Assert.Equal(1, height);
    }

    [Fact]
    public void TryRead_WhenJpeg_ReturnsWidthAndHeight()
    {
        using var stream = new MemoryStream(SampleImages.ValidJpegHeader32x16);

        var ok = ImageDimensionsReader.TryRead(stream, "image/jpeg", out var width, out var height);

        Assert.True(ok);
        Assert.Equal(32, width);
        Assert.Equal(16, height);
    }

    [Fact]
    public void TryRead_WhenWebpExtended_ReturnsWidthAndHeight()
    {
        using var stream = new MemoryStream(SampleImages.ValidWebpVp8x100x200);

        var ok = ImageDimensionsReader.TryRead(stream, "image/webp", out var width, out var height);

        Assert.True(ok);
        Assert.Equal(100, width);
        Assert.Equal(200, height);
    }

    [Fact]
    public void TryRead_WhenContentIsNotThatFormat_ReturnsFalseWithoutThrowing()
    {
        using var stream = new MemoryStream(SampleImages.NotAnImage);

        var ok = ImageDimensionsReader.TryRead(stream, "image/png", out var width, out var height);

        Assert.False(ok);
        Assert.Equal(0, width);
        Assert.Equal(0, height);
    }

    [Fact]
    public void TryRead_LeavesStreamPositionUnchanged()
    {
        using var stream = new MemoryStream(SampleImages.ValidPng1x1);
        stream.Position = 3;

        ImageDimensionsReader.TryRead(stream, "image/png", out _, out _);

        Assert.Equal(3, stream.Position);
    }
}
