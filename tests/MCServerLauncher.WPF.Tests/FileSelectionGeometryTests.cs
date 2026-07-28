using System.Windows;
using MCServerLauncher.WPF.InstanceConsole.View.Pages;

namespace MCServerLauncher.WPF.Tests;

public sealed class FileSelectionGeometryTests
{
    [Fact]
    public void SelectionRectangleRecognizesIntersectingItems()
    {
        var selection = new Rect(10, 10, 80, 40);

        Assert.True(FileManagerPage.IntersectsSelection(selection, new Rect(20, 20, 60, 20)));
        Assert.False(FileManagerPage.IntersectsSelection(selection, new Rect(100, 20, 30, 20)));
    }
}
