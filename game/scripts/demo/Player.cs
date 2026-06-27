using Godot;
using Scrapline.Core;

namespace Scrapline.Game.Demo;

/// <summary>
/// A WASD-driven rectangle. This is the whole job of the Godot layer: read input,
/// hand a plain direction to the engine-independent Core (<see cref="Mover"/>),
/// then apply whatever Core returns. No movement math lives here — that's tested
/// in Core.Tests. This script is the "presentation" half of the seam.
/// </summary>
public partial class Player : Polygon2D
{
    /// <summary>Movement speed in pixels/second. Editable in the Inspector.</summary>
    [Export] public float Speed { get; set; } = 300f;

    public override void _Ready()
    {
        GD.Print("WASD to move the rectangle. Press Esc (or the Stop button) to quit.");
    }

    public override void _Process(double delta)
    {
        if (Input.IsKeyPressed(Key.Escape))
        {
            GetTree().Quit();
            return;
        }

        // Read raw WASD into a direction. Physical keys = same physical positions
        // regardless of keyboard layout (AZERTY, etc.).
        int x = (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0);
        int y = (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0);
        var input = new System.Numerics.Vector2(x, y);

        // Cross the seam: Godot.Vector2 -> System.Numerics.Vector2 -> Core -> back.
        System.Numerics.Vector2 next = Mover.Step(
            position: new System.Numerics.Vector2(Position.X, Position.Y),
            input: input,
            speed: Speed,
            deltaSeconds: (float)delta);

        Position = new Vector2(next.X, next.Y);
    }
}
