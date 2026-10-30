using UnityEngine;

/// <summary>
/// Reads a leaf model's shape (2026-10-30): the midrib the spring fold turns the blade about, and
/// the blade's size. Shared by <see cref="LeafManager"/> in play and the leaf importer in the
/// editor. A leaf model's blade is submesh 0 and its stalk, if any, submesh 1, and the model's
/// origin is the stalk's twig end.
/// </summary>
public static class LeafModel
{
    /// <summary>The midrib in mesh space: from where the stalk meets the blade — the stalk's far end,
    /// or with no stalk the blade point nearest the origin — toward the blade point farthest from
    /// there (the main lobe's tip). <paramref name="up"/> is the way the blade's top faces, from its
    /// normals, and <paramref name="axis"/> is square to it. Needs a Read/Write mesh.</summary>
    public static bool TryMidrib(Mesh mesh, out Vector3 junction, out Vector3 axis, out Vector3 up)
    {
        junction = axis = up = Vector3.zero;
        if (mesh == null || !mesh.isReadable) return false;
        var verts   = mesh.vertices;
        var normals = mesh.normals;
        if (normals.Length != verts.Length) return false;
        var blade   = mesh.GetTriangles(0);

        if (mesh.subMeshCount > 1)
        {
            float best = -1f;
            foreach (int i in mesh.GetTriangles(1))
                if (verts[i].sqrMagnitude > best) { best = verts[i].sqrMagnitude; junction = verts[i]; }
        }
        else
        {
            float best = float.MaxValue;
            foreach (int i in blade)
                if (verts[i].sqrMagnitude < best) { best = verts[i].sqrMagnitude; junction = verts[i]; }
        }

        Vector3 tip = junction;
        float   far = 0f;
        foreach (int i in blade)
        {
            float d = (verts[i] - junction).sqrMagnitude;
            if (d > far) { far = d; tip = verts[i]; }
            up += normals[i];
        }
        axis = Vector3.ProjectOnPlane(tip - junction, up);
        if (up.sqrMagnitude < 1e-12f || axis.sqrMagnitude < 1e-12f) return false;
        axis.Normalize();
        up.Normalize();
        return true;
    }

    /// <summary>How big a leaf prefab's blade is as spawned: the diagonal of the blade submesh's
    /// bounds, times the scale of the transforms between the prefab root and the mesh (the root's
    /// own scale is replaced when a leaf spawns). Works on meshes that aren't Read/Write.</summary>
    public static float BladeSpan(GameObject prefab)
    {
        var mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null || mf.sharedMesh.subMeshCount == 0) return 0f;
        float scale = 1f;
        for (var t = mf.transform; t != null && t != prefab.transform; t = t.parent)
            scale *= Mathf.Abs(t.localScale.x);
        return mf.sharedMesh.GetSubMesh(0).bounds.size.magnitude * scale;
    }

    /// <summary>The turn that lays a model's blade the way leaves expect, for the child under the
    /// prefab root: blade (midrib) along +Z, width along Y — which the spring unfold squeezes — and
    /// top along +X, which the maple roll turns to the sky.</summary>
    public static Quaternion LayFlat(Vector3 axis, Vector3 up) =>
        Quaternion.Inverse(Quaternion.LookRotation(axis, Vector3.Cross(axis, up)));
}
