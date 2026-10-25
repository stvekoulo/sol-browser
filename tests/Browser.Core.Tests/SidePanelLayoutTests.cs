using Browser.Core;
using System.Text.Json;

namespace Browser.Core.Tests;

public sealed class SidePanelLayoutTests
{
    [Theory]
    [InlineData(0.86)]
    [InlineData(0.92)]
    [InlineData(1.5)]
    public void Oversized_drag_requests_are_capped_and_leave_room_for_the_page(double scale)
    {
        Assert.Equal(360 * scale, SidePanelLayout.Sidebar(10000, 2000 * scale, scale));
        var available = 700 * scale;
        var chat = SidePanelLayout.Chat(10000, available, scale, overlay: false);
        Assert.True(chat <= 480 * scale);
        Assert.True(available - chat >= 320 * scale);
        Assert.Equal(480 * scale, SidePanelLayout.Chat(10000, 1000 * scale, scale, overlay: true));
    }

    [Fact]
    public void Narrow_windows_reduce_panels_without_losing_the_requested_width()
    {
        var settings = new Settings { SidebarWidth = 360, ChatWidth = 480 };
        Assert.True(SidePanelLayout.Sidebar(settings.SidebarWidth, 800, 1) < settings.SidebarWidth);
        Assert.Equal(360, SidePanelLayout.Sidebar(settings.SidebarWidth, 1600, 1));
        Assert.Equal(200, SidePanelLayout.Chat(settings.ChatWidth, 200, 1, overlay: true));
        Assert.Equal(480, settings.ChatWidth);
        Assert.Equal(0, SidePanelLayout.Chat(settings.ChatWidth, 0, 1, overlay: true));
    }

    [Fact]
    public void Old_profiles_and_invalid_values_cannot_restore_oversized_panels()
    {
        var restored = JsonSerializer.Deserialize<Settings>("{\"SidebarWidth\":520,\"ChatWidth\":760}")!;
        Assert.Equal(360, restored.SidebarWidth);
        Assert.Equal(480, restored.ChatWidth);
        restored.SidebarWidth = double.NaN;
        restored.ChatWidth = double.PositiveInfinity;
        Assert.Equal(296, restored.SidebarWidth);
        Assert.Equal(380, restored.ChatWidth);
    }
}
