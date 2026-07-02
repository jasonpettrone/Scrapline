using System.Linq;
using System.Numerics;
using Scrapline.Core.Combat;
using Scrapline.Core.Destruction;
using Xunit;

namespace Scrapline.Core.Tests.Destruction;

public class DeformableSilhouetteTests
{
    // A simple, exaggerated profile with round numbers so the dent math is obvious:
    // push = 0.01 px per unit force (before falloff), capped at 10 px, radius 50,
    // ease fully in one Step at rate large enough, panel sheds at 100 accumulated force.
    private static DeformationProfile Profile => new()
    {
        CrumpleScale = 0.01f,
        MaxCrumpleDepth = 10f,
        FlatEdgeFalloff = 5f,
        SharpReach = 16f,
        EaseRate = 12f,
        PanelShedThreshold = 100f,
        ShedsWhileAlive = true,
    };

    // A 100×60 box (half-extents 50×30) with 4 verts per edge → 16 verts, centred on origin.
    private static DeformableSilhouette Box() => DeformableSilhouette.Box(50f, 30f, 4, Profile);

    // Drive the easing to convergence so CurrentVertices reflects the full accumulated dent.
    private static void Settle(DeformableSilhouette s)
    {
        for (int i = 0; i < 200; i++)
            s.Step(1f / 60f);
    }

    [Fact]
    public void A_fresh_silhouette_sits_at_its_rest_shape()
    {
        var s = Box();
        Assert.Equal(0f, s.CrumpleAmount, 5);

        var verts = s.CurrentVertices();
        for (int i = 0; i < s.VertexCount; i++)
            Assert.Equal(s.RestVertex(i), verts[i]);
    }

    [Fact]
    public void A_hit_dents_vertices_inward_toward_the_centre()
    {
        var s = Box();
        // Hit the front-right area; the nearest vertex should move toward the centre (origin).
        var contact = new Vector2(50f, 0f); // mid front edge
        s.ApplyHit(contact, force: 500f, ImpactZone.Front);
        Settle(s);

        var verts = s.CurrentVertices();
        // Find the vertex nearest the contact and confirm it pulled inward (|pos| shrank).
        int nearest = NearestRestVertex(s, contact);
        float restDist = s.RestVertex(nearest).Length();
        float nowDist = verts[nearest].Length();
        Assert.True(nowDist < restDist, $"vertex should move inward: was {restDist}, now {nowDist}");
    }

    [Fact]
    public void A_harder_hit_dents_deeper()
    {
        var contact = new Vector2(50f, 0f);

        var soft = Box();
        soft.ApplyHit(contact, force: 200f, ImpactZone.Front);
        Settle(soft);

        var hard = Box();
        hard.ApplyHit(contact, force: 800f, ImpactZone.Front);
        Settle(hard);

        Assert.True(hard.CrumpleAmount > soft.CrumpleAmount,
            $"hard {hard.CrumpleAmount} should exceed soft {soft.CrumpleAmount}");
    }

    [Fact]
    public void Damage_accumulates_across_repeated_hits()
    {
        var s = Box();
        var contact = new Vector2(50f, 0f);

        s.ApplyHit(contact, force: 200f, ImpactZone.Front);
        Settle(s);
        float afterOne = s.CrumpleAmount;

        s.ApplyHit(contact, force: 200f, ImpactZone.Front);
        Settle(s);
        float afterTwo = s.CrumpleAmount;

        Assert.True(afterTwo > afterOne, "a second hit on the same spot should deepen the dent");
    }

    [Fact]
    public void A_dent_never_exceeds_the_depth_cap()
    {
        var s = Box();
        var contact = new Vector2(50f, 0f);

        // Hammer it far past the cap.
        for (int i = 0; i < 50; i++)
            s.ApplyHit(contact, force: 1000f, ImpactZone.Front);
        Settle(s);

        // No vertex may move more than the cap (10px) from its rest position.
        var verts = s.CurrentVertices();
        for (int i = 0; i < s.VertexCount; i++)
        {
            float moved = (verts[i] - s.RestVertex(i)).Length();
            Assert.True(moved <= Profile.MaxCrumpleDepth + 0.001f, $"vertex {i} moved {moved}, over cap");
        }
    }

    [Fact]
    public void A_dent_never_folds_through_the_centre()
    {
        // A corner vertex is only ~58px from centre; even hammered, its inward move is capped to a
        // fraction of that, so it can't cross the origin and invert the shape.
        var s = Box();
        var corner = new Vector2(50f, 30f);
        for (int i = 0; i < 50; i++)
            s.ApplyHit(corner, force: 1000f, ImpactZone.Side);
        Settle(s);

        int idx = NearestRestVertex(s, corner);
        var rest = s.RestVertex(idx);
        var now = s.CurrentVertices()[idx];
        // Still on the same side of the centre on both axes (didn't pass through origin).
        Assert.True(System.MathF.Sign(now.X) == System.MathF.Sign(rest.X) || now.X == 0f);
        Assert.True(System.MathF.Sign(now.Y) == System.MathF.Sign(rest.Y) || now.Y == 0f);
    }

    [Fact]
    public void The_dent_eases_in_rather_than_snapping()
    {
        var s = Box();
        s.ApplyHit(new Vector2(50f, 0f), force: 800f, ImpactZone.Front);

        float afterOneFrame = AfterStep(s, 1f / 60f);
        Settle(s);
        float settled = s.CrumpleAmount;

        Assert.True(afterOneFrame > 0f, "some dent should appear immediately");
        Assert.True(afterOneFrame < settled, "but one frame shouldn't reach the full settled dent");
    }

    [Fact]
    public void Vertices_are_tagged_front_side_and_rear()
    {
        var s = Box();
        var zones = Enumerable.Range(0, s.VertexCount).Select(s.ZoneOf).ToHashSet();
        Assert.Contains(ImpactZone.Front, zones);
        Assert.Contains(ImpactZone.Side, zones);
        Assert.Contains(ImpactZone.Rear, zones);

        // A vertex out front (+X) is Front; one out back (-X) is Rear.
        int front = NearestRestVertex(s, new Vector2(50f, 0f));
        int rear = NearestRestVertex(s, new Vector2(-50f, 0f));
        Assert.Equal(ImpactZone.Front, s.ZoneOf(front));
        Assert.Equal(ImpactZone.Rear, s.ZoneOf(rear));
    }

    [Fact]
    public void A_panel_sheds_once_its_zone_passes_the_threshold()
    {
        var s = Box();
        var contact = new Vector2(50f, 0f);

        // First hit (force 60) is under the 100 threshold → nothing sheds yet.
        s.ApplyHit(contact, force: 60f, ImpactZone.Front);
        Assert.Empty(s.ConsumeNewlyShedPanels());

        // Second hit pushes the front zone's accrued force over 100 → the front panel sheds once.
        s.ApplyHit(contact, force: 60f, ImpactZone.Front);
        var shed = s.ConsumeNewlyShedPanels();
        Assert.Equal(new[] { ImpactZone.Front }, shed);

        // It only sheds once; further hits on the gone panel report nothing.
        s.ApplyHit(contact, force: 500f, ImpactZone.Front);
        Assert.Empty(s.ConsumeNewlyShedPanels());
    }

    [Fact]
    public void A_sober_profile_never_sheds_panels_while_alive()
    {
        var s = DeformableSilhouette.Box(50f, 30f, 4, DeformationProfile.Sober);
        for (int i = 0; i < 50; i++)
            s.ApplyHit(new Vector2(50f, 0f), force: 1000f, ImpactZone.Front);

        Assert.Empty(s.ConsumeNewlyShedPanels());
    }

    [Fact]
    public void Shedding_all_panels_on_death_returns_every_remaining_panel()
    {
        var s = DeformableSilhouette.Box(50f, 30f, 4, DeformationProfile.Sober);
        var shed = s.ShedAllPanels();

        Assert.Equal(3, shed.Count);
        Assert.Contains(ImpactZone.Front, shed);
        Assert.Contains(ImpactZone.Side, shed);
        Assert.Contains(ImpactZone.Rear, shed);

        // Already gone — a second call returns nothing.
        Assert.Empty(s.ShedAllPanels());
    }

    [Fact]
    public void Repair_restores_the_pristine_shape_and_resets_shedding()
    {
        var s = Box();
        var contact = new Vector2(50f, 0f);
        for (int i = 0; i < 5; i++)
            s.ApplyHit(contact, force: 500f, ImpactZone.Front);
        Settle(s);
        Assert.True(s.CrumpleAmount > 0f);

        s.Repair();
        Settle(s);

        Assert.Equal(0f, s.CrumpleAmount, 5);
        var verts = s.CurrentVertices();
        for (int i = 0; i < s.VertexCount; i++)
            Assert.Equal(s.RestVertex(i), verts[i]);

        // Front panel can shed afresh after a repair.
        for (int i = 0; i < 3; i++)
            s.ApplyHit(contact, force: 60f, ImpactZone.Front);
        Assert.Contains(ImpactZone.Front, s.ConsumeNewlyShedPanels());
    }

    // ── Calibration: the real presets must produce VISIBLE dents on the HP scale ──
    // The Phase-2 bug was feeding HP damage (tens) into a scale tuned for closing speed (hundreds),
    // yielding sub-pixel, invisible dents. These guard the magnitude against drifting back.

    [Fact]
    public void An_exaggerated_car_visibly_caves_from_one_solid_ram()
    {
        // A 40 HP ram to the front edge of a placeholder-sized car (96×48) must move the struck
        // vertices several pixels — clearly visible, not sub-pixel.
        var s = DeformableSilhouette.Box(48f, 24f, 4, DeformationProfile.Exaggerated);
        s.ApplyHit(new Vector2(48f, 0f), force: 40f, ImpactZone.Front);
        Settle(s);

        Assert.True(MaxDisplacement(s) > 8f, $"a 40 HP ram should cave > 8px, got {MaxDisplacement(s)}px");
    }

    [Fact]
    public void A_sober_car_dents_subtly_from_a_small_chip()
    {
        // A small 5 HP chip to the player should register, but stay restrained (readable).
        var s = DeformableSilhouette.Box(48f, 24f, 4, DeformationProfile.Sober);
        s.ApplyHit(new Vector2(48f, 0f), force: 5f, ImpactZone.Front);
        Settle(s);

        float moved = MaxDisplacement(s);
        Assert.True(moved > 0.3f, $"a chip should still dent, got {moved}px");
        Assert.True(moved < 4f, $"but the player should stay sober, got {moved}px");
    }

    // ── Density builder (for walls' extreme aspect ratios) ────────────────────────

    [Fact]
    public void Spacing_builder_spreads_vertices_evenly_and_caps_the_count()
    {
        // A long thin wall (3600×100, half 1800×50) at 50px spacing: the long edges cap at maxPerEdge,
        // the short edges get only a couple — no cramming on the thin side.
        var s = DeformableSilhouette.BoxWithSpacing(1800f, 50f, spacing: 50f, DeformationProfile.Wall, maxPerEdge: 40);

        // 40 (top) + 2 (right) + 40 (bottom) + 2 (left) = 84.
        Assert.Equal(84, s.VertexCount);

        // Still a valid ring centred at the origin.
        Assert.Equal(0f, s.Centroid.X, 3);
        Assert.Equal(0f, s.Centroid.Y, 3);
    }

    [Fact]
    public void Spacing_builder_dents_only_the_struck_face_on_a_wall()
    {
        var s = DeformableSilhouette.BoxWithSpacing(400f, 40f, 50f, DeformationProfile.Wall);
        // Hit the top face pushing down (+Y is down in Godot space).
        s.ApplyHit(new Indenter(new Vector2(0f, -40f), new Vector2(0f, 1f), HalfWidth: 24f, Sharpness: 0f),
            force: 400f, ImpactZone.Front);
        Settle(s);

        int top = NearestRestVertex(s, new Vector2(0f, -40f));
        int bottom = NearestRestVertex(s, new Vector2(0f, 40f));
        Assert.True(s.Offset(top).Length() > 1f, "struck top face should dent");
        Assert.True(s.Offset(bottom).Length() < 0.01f, "opposite face must stay put");
    }

    // ── Localization & impactor shape (the directional model) ─────────────────────

    [Fact]
    public void Only_the_struck_side_caves_not_the_opposite_face()
    {
        // The Phase-2.1 bug: hitting one side crumpled the other. The struck-half gate must keep the
        // far face perfectly still.
        var s = DeformableSilhouette.Box(50f, 30f, 6, Profile);
        var frontHit = new Indenter(new Vector2(50f, 0f), new Vector2(-1f, 0f), HalfWidth: 20f, Sharpness: 0f);
        for (int i = 0; i < 5; i++)
            s.ApplyHit(frontHit, force: 500f, ImpactZone.Front);
        Settle(s);

        int rear = NearestRestVertex(s, new Vector2(-50f, 0f));
        Assert.True((s.CurrentVertices()[rear] - s.RestVertex(rear)).Length() < 0.01f, "the rear face must not move");

        int front = NearestRestVertex(s, new Vector2(50f, 0f));
        Assert.True((s.CurrentVertices()[front] - s.RestVertex(front)).Length() > 1f, "the struck front should cave");
    }

    [Fact]
    public void A_flat_impactor_dents_more_broadly_than_a_corner()
    {
        // Same contact and depth; a flat face (sharpness 0, wide) spreads across many vertices, a
        // corner (sharpness 1, narrow) concentrates on a few — a wide rectangular vs a pointed imprint.
        var contact = new Vector2(50f, 0f);
        var push = new Vector2(-1f, 0f);

        var flat = DeformableSilhouette.Box(50f, 30f, 8, Profile);
        flat.ApplyHit(new Indenter(contact, push, HalfWidth: 24f, Sharpness: 0f), force: 500f, ImpactZone.Front);
        Settle(flat);

        var corner = DeformableSilhouette.Box(50f, 30f, 8, Profile);
        corner.ApplyHit(new Indenter(contact, push, HalfWidth: 24f, Sharpness: 1f), force: 500f, ImpactZone.Front);
        Settle(corner);

        Assert.True(MovedVertexCount(flat) > MovedVertexCount(corner),
            $"flat moved {MovedVertexCount(flat)} verts, corner moved {MovedVertexCount(corner)}");
    }

    [Fact]
    public void Vertices_push_along_the_impact_normal_not_toward_the_centre()
    {
        // An off-centre hit pushing straight in (-X) must move vertices purely along -X — proof the
        // dent follows the impact normal, not a line to the centroid (which would add Y drift).
        var s = DeformableSilhouette.Box(50f, 30f, 8, Profile);
        var contact = new Vector2(50f, 20f);
        s.ApplyHit(new Indenter(contact, new Vector2(-1f, 0f), HalfWidth: 12f, Sharpness: 0f), force: 500f, ImpactZone.Front);
        Settle(s);

        Vector2 off = s.Offset(NearestRestVertex(s, contact));
        Assert.True(off.X < -1f, $"should drive inward along -X, got {off.X}");
        Assert.True(System.MathF.Abs(off.Y) < 0.01f, $"should not drift sideways, got {off.Y}");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private static int MovedVertexCount(DeformableSilhouette s, float threshold = 0.5f)
    {
        int n = 0;
        for (int i = 0; i < s.VertexCount; i++)
            if (s.Offset(i).Length() > threshold)
                n++;
        return n;
    }

    private static float MaxDisplacement(DeformableSilhouette s)
    {
        var verts = s.CurrentVertices();
        float max = 0f;
        for (int i = 0; i < s.VertexCount; i++)
            max = System.MathF.Max(max, (verts[i] - s.RestVertex(i)).Length());
        return max;
    }

    private static int NearestRestVertex(DeformableSilhouette s, Vector2 p)
    {
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < s.VertexCount; i++)
        {
            float d = (s.RestVertex(i) - p).LengthSquared();
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private static float AfterStep(DeformableSilhouette s, float dt)
    {
        s.Step(dt);
        return s.CrumpleAmount;
    }
}
