using UnityEngine;

/// <summary>
/// CPU mirror of the branch-sway wind field.
///
/// ══════════════════════════════════════════════════════════════════════════════════════
///  MIRROR CONTRACT — this file, TreeBud.shader and the HLSLINCLUDE block in BarkVertexColor.shader
///  (TreeFlex / TreeSway) implement the SAME two functions with the SAME constants.
///  Any change here must be made there in the same commit, and vice versa. If they drift,
///  leaves and wires detach from the bark they ride — the bark bends on the GPU while the
///  CPU riders compute a different bend.
/// ══════════════════════════════════════════════════════════════════════════════════════
///
/// Both sides read one authoritative parameter set: WindManager publishes it per frame as
/// shader globals (_TreeWindA/_TreeWindB/_TreeWindReachSpan) for the GPU and as Canon*
/// statics for this class. Phase is derived from REST world position, so the field is
/// continuous in space — neighbouring vertices sway near-identically (no tearing) and
/// anything anchored at a point can compute the exact displacement of that point.
/// </summary>
public static class WindSway
{
    /// <summary>How much a point of the tree yields to wind, 0 (rigid) … 1 (whippy tip).
    /// Derived from data every bark vertex already carries — no baked channel:
    ///   barkBlendA — vertex color.a, 0 = young thin growth, 1 = mature thick bark
    ///                (GrowthColor: max of radius-blend and age-blend, a real stiffness proxy);
    ///   isRootBit  — vertex color.r, roots are buried and never sway;
    ///   posOS      — object-space position: height pins the base, horizontal reach lets
    ///                aged-but-thin outer branchwood sway even though its bark reads mature.
    /// Mirror of TreeFlex() in BarkVertexColor.shader.</summary>
    public static float Flex(float barkBlendA, float isRootBit, Vector3 posOS)
    {
        float youngness = 1f - barkBlendA;
        float reach = Mathf.Clamp01(
            (new Vector2(posOS.x, posOS.z).magnitude - WindManager.CanonReachIn)
            / Mathf.Max(WindManager.CanonReachSpan, 0.001f));
        float anchor = Mathf.Max(
            Mathf.Clamp01(posOS.y / Mathf.Max(WindManager.CanonHeightRef, 0.001f)), reach);
        return (1f - isRootBit) * anchor * Mathf.Max(youngness, reach);
    }

    /// <summary>World-space displacement of a tree point at rest position
    /// <paramref name="restWS"/> with the given flex. Mirror of TreeSway() in
    /// BarkVertexColor.shader — constants must stay byte-identical with it.</summary>
    public static Vector3 Sway(Vector3 restWS, float flex)
    {
        float strength = WindManager.CanonStrength;
        if (strength <= 0f || flex <= 0f) return Vector3.zero;

        float t     = WindManager.CanonTime;
        Vector3 w   = WindManager.CanonDir;                       // xz used, unit length
        float phase = restWS.x * 1.7f + restWS.z * 2.3f;
        float pB    = restWS.x * 0.9f - restWS.z * 1.3f;
        float sMain = Mathf.Sin(t * 1.9f + phase) * 0.6f + Mathf.Sin(t * 3.7f + phase * 1.31f) * 0.4f;
        float sPerp = Mathf.Sin(t * 2.6f + pB);
        float amp   = WindManager.CanonAmpMax * strength * (0.25f + 0.75f * WindManager.CanonGustEnv);
        float f2    = flex * flex;

        Vector3 d = new Vector3(w.x, 0f, w.z) * (sMain * amp * f2)
                  + new Vector3(-w.z, 0f, w.x) * (sPerp * amp * 0.35f * f2);
        d.y -= Mathf.Abs(sMain) * amp * 0.18f * f2;               // bend is an arc: tips settle down
        return d;
    }
}
