using Godot;
using PumpkinFace.Display.Rendering;

namespace PumpkinFace.Display.Tests;

public sealed class CameraOrbitControllerTests
{
    [Fact]
    public void DragIsBoundedAndReturnsAfterFiveIdleSeconds()
    {
        CameraOrbitController controller = new();
        controller.Drag(new Vector2(1000f, -1000f));

        Assert.Equal(new Vector2(60f, 38f), controller.Degrees);
        Assert.False(controller.Update(5.0));
        Assert.Equal(new Vector2(60f, 38f), controller.Degrees);

        Assert.True(controller.Update(0.1));
        Assert.True(controller.IsReturning);
        Assert.True(controller.Degrees.Length() < new Vector2(60f, 38f).Length());

        controller.Update(1.0);
        Assert.Equal(Vector2.Zero, controller.Degrees);
    }

    [Fact]
    public void ANewDragRestartsTheIdleTimer()
    {
        CameraOrbitController controller = new();
        controller.Drag(new Vector2(80f, 0f));
        controller.Update(4.9);
        controller.Drag(new Vector2(5f, 0f));
        Vector2 afterSecondDrag = controller.Degrees;

        Assert.False(controller.Update(4.9));
        Assert.Equal(afterSecondDrag, controller.Degrees);
    }
}
