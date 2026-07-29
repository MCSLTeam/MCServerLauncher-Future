using System.Windows;
using MCServerLauncher.WPF.InstanceConsole.View.Pages;

namespace MCServerLauncher.WPF.Tests;

public sealed class ComponentSelectionGeometryTests
{
    [Fact]
    public void SelectionPointIsClampedToListBounds()
    {
        var clamped = ComponentManagerPage.ClampSelectionPoint(new Point(-10, 250), new Size(100, 200));

        Assert.Equal(new Point(0, 200), clamped);
    }

    [Fact]
    public void BoxScrollStepScalesWithDistanceAndHasAnUpperBound()
    {
        Assert.Equal(0, ComponentManagerPage.GetBoxScrollStep(0));
        Assert.True(ComponentManagerPage.GetBoxScrollStep(80) > ComponentManagerPage.GetBoxScrollStep(10));
        Assert.Equal(14, ComponentManagerPage.GetBoxScrollStep(1000));
    }
}
