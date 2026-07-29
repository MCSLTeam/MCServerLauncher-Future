using System.Windows;
using MCServerLauncher.WPF.InstanceConsole.View.Pages;

namespace MCServerLauncher.WPF.Tests;

public sealed class EventTriggerSelectionGeometryTests
{
    [Fact]
    public void SelectionPointIsClampedToListBounds()
    {
        var clamped = EventTriggerPage.ClampSelectionPoint(new Point(-10, 250), new Size(100, 200));

        Assert.Equal(new Point(0, 200), clamped);
    }

    [Fact]
    public void BoxScrollStepScalesWithDistanceAndHasAnUpperBound()
    {
        Assert.Equal(0, EventTriggerPage.GetBoxScrollStep(0));
        Assert.True(EventTriggerPage.GetBoxScrollStep(80) > EventTriggerPage.GetBoxScrollStep(10));
        Assert.Equal(14, EventTriggerPage.GetBoxScrollStep(1000));
    }
}