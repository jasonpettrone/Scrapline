using System.Numerics;

namespace Scrapline.Core.Destruction;

/// <summary>
/// Describes <em>what</em> struck the car and how, so the dent reflects the impactor's shape
/// (docs/09): a flat face leaves a wide, flat-bottomed imprint; a corner leaves a narrow, pointed
/// one. All in the victim car's local frame. The Godot layer builds this from the two bodies'
/// geometry; <see cref="DeformableSilhouette.ApplyHit(in Indenter, float, Scrapline.Core.Combat.ImpactZone)"/>
/// turns it into vertex motion.
/// </summary>
/// <param name="ContactPoint">Where the hit landed, on/near the struck face (local space).</param>
/// <param name="PushDir">Unit direction the surface is driven — <em>into</em> the car (the inward
/// face normal). Vertices move along this, so the dent floor is flat and correctly oriented (this
/// is what stops the far side from caving).</param>
/// <param name="HalfWidth">Half-width (px) of the full-depth flat region along the surface — the
/// breadth of the impactor's contacting face. Wide for a flat slam, ~0 for a corner.</param>
/// <param name="Sharpness">0 = flat/rectangular imprint (flat top, crisp edges); 1 = pointed/
/// triangular imprint (peak at the contact, straight V sides).</param>
public readonly record struct Indenter(
    Vector2 ContactPoint,
    Vector2 PushDir,
    float HalfWidth,
    float Sharpness);
