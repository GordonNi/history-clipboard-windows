using HistoryClipboard.App.Windows;

namespace HistoryClipboard.Tests;

public sealed class WindowPlacementGeometryTests
{
    [Theory]
    [InlineData(96u, 420, 680, 16, -1920, 0, 0, 1080, -436, 384, 420, 680)]
    [InlineData(120u, 420, 680, 16, 0, 0, 2560, 1400, 2015, 530, 525, 850)]
    [InlineData(144u, 420, 680, 16, 0, 0, 2560, 1400, 1906, 356, 630, 1020)]
    [InlineData(192u, 420, 680, 16, 3840, -120, 6400, 2040, 5528, 648, 840, 1360)]
    public void PlacesPopupInsidePhysicalWorkAreaAtCommonDpiScales(
        uint dpi,
        double widthDip,
        double heightDip,
        double marginDip,
        int workLeft,
        int workTop,
        int workRight,
        int workBottom,
        int expectedLeft,
        int expectedTop,
        int expectedWidth,
        int expectedHeight)
    {
        var placement = WindowPlacementGeometry.PlaceBottomRight(
            new PhysicalRectangle(workLeft, workTop, workRight, workBottom),
            dpi,
            dpi,
            widthDip,
            heightDip,
            marginDip);

        Assert.Equal(
            new PhysicalWindowBounds(
                expectedLeft,
                expectedTop,
                expectedWidth,
                expectedHeight),
            placement);
        Assert.True(placement.Left >= workLeft);
        Assert.True(placement.Top >= workTop);
        Assert.True(placement.Right <= workRight);
        Assert.True(placement.Bottom <= workBottom);
    }

    [Fact]
    public void OversizedPopupIsClampedToWorkArea()
    {
        var placement = WindowPlacementGeometry.PlaceBottomRight(
            new PhysicalRectangle(-1280, -720, 0, 0),
            192,
            192,
            900,
            600,
            16);

        Assert.Equal(new PhysicalWindowBounds(-1280, -720, 1280, 720), placement);
    }

    [Theory]
    [InlineData(0u, 96u)]
    [InlineData(96u, 0u)]
    public void RejectsInvalidDpi(uint dpiX, uint dpiY)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindowPlacementGeometry.PlaceBottomRight(
                new PhysicalRectangle(0, 0, 1920, 1080),
                dpiX,
                dpiY,
                420,
                680,
                16));
    }
}
