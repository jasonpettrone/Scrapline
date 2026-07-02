namespace Scrapline.Game.Tests;

using GdUnit4;
using Godot;
using Scrapline.Game.Race;

using static GdUnit4.Assertions;

/// <summary>
/// In-engine checks for the wall side of destruction (docs/09 "environment as a full participant"):
/// the dent must appear where the car actually struck. Regression for the face-selection bug where
/// a long wall resolved almost every contact to its END CAP (the face was picked by direction from
/// the wall's centre, which on a 3600×50 wall points along its length for nearly any hit).
/// </summary>
[TestSuite]
public class DeformableWallTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void An_offcentre_hit_dents_the_long_face_at_the_contact_not_the_end_cap()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var root = AutoFree(new Node2D())!;
        tree.Root.AddChild(root);

        var wall = new DeformableWall { Size = new Vector2(2000f, 100f) };
        root.AddChild(wall); // _Ready runs here and builds the silhouette

        // Slam the top face 600px right of centre, driving straight down (a square-on hit).
        wall.TakeImpact(wall.ToGlobal(new Vector2(600f, -50f)), new Vector2(0f, 400f), 400f);

        var s = wall.Deformer!.Silhouette;
        for (int i = 0; i < 300; i++)
            s.Step(1f / 60f); // settle the ease directly — no physics frames needed

        AssertFloat(OffsetNear(s, new Vector2(600f, -50f)).Length())
            .OverrideFailureMessage("the top face at the contact point should dent")
            .IsGreater(3f);
        AssertFloat(OffsetNear(s, new Vector2(1000f, 0f)).Length())
            .OverrideFailureMessage("the right end cap must not move — that was the bug")
            .IsLess(0.01f);
        AssertFloat(OffsetNear(s, new Vector2(600f, 50f)).Length())
            .OverrideFailureMessage("the opposite (bottom) face must stay put")
            .IsLess(0.01f);
    }

    /// <summary>The eased offset of the silhouette vertex nearest <paramref name="p"/> (local).</summary>
    private static Vector2 OffsetNear(Scrapline.Core.Destruction.DeformableSilhouette s, Vector2 p)
    {
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < s.VertexCount; i++)
        {
            var r = s.RestVertex(i);
            float d = (new Vector2(r.X, r.Y) - p).LengthSquared();
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        var off = s.Offset(best);
        return new Vector2(off.X, off.Y);
    }
}
