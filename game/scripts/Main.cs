using Godot;
using Scrapline.Core;

namespace Scrapline.Game;

/// <summary>
/// The M0 smoke scene's controller. Its whole job right now is to prove the
/// Godot presentation layer boots and can reach into the engine-independent Core.
/// </summary>
public partial class Main : Node2D
{
    public override void _Ready()
    {
        string message = BuildInfo.Greeting();

        // Printed to stdout so the headless CI smoke test can assert on it.
        GD.Print(message);

        GetNode<Label>("Label").Text = message;
    }
}
