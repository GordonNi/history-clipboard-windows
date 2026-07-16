using HistoryClipboard.Core;

namespace HistoryClipboard.Tests;

public sealed class ProjectBaselineTests
{
    [Fact]
    public void ProductIdentityIsStable()
    {
        Assert.Equal("历史粘贴板", ProductInfo.DisplayName);
        Assert.Equal("HistoryClipboard", ProductInfo.EnglishName);
        Assert.Equal(1, ProductInfo.SyncProtocolVersion);
    }
}
