using System.Windows;
using MCServerLauncher.WPF.View.Pages;

namespace MCServerLauncher.WPF.Tests;

public sealed class ManagerSelectionGeometryTests
{
    [Fact]
    public void ManagerSelectionPointsAreClampedToListBounds()
    {
        var point = new Point(-10, 250);
        var bounds = new Size(100, 200);

        Assert.Equal(new Point(0, 200), InstanceManagerPage.ClampSelectionPoint(point, bounds));
        Assert.Equal(new Point(0, 200), DaemonManagerPage.ClampSelectionPoint(point, bounds));
    }

    [Fact]
    public void ManagerBoxScrollStepScalesWithDistanceAndHasAnUpperBound()
    {
        Assert.True(InstanceManagerPage.GetBoxScrollStep(80) > InstanceManagerPage.GetBoxScrollStep(10));
        Assert.True(DaemonManagerPage.GetBoxScrollStep(80) > DaemonManagerPage.GetBoxScrollStep(10));
        Assert.Equal(14, InstanceManagerPage.GetBoxScrollStep(1000));
        Assert.Equal(14, DaemonManagerPage.GetBoxScrollStep(1000));
    }
}
