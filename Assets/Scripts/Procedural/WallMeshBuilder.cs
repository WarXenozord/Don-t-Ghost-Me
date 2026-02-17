using UnityEngine;
using System.Collections.Generic;
public static class WallMeshBuilder
{
    public static Mesh BuildWallWithOpening(
        float start,
        float end,
        float height,
        float thickness,
        float doorStart,
        float doorEnd,
        float doorHeight,
        bool runsAlongX
    )
    {
        Mesh mesh = new Mesh();

        List<Vector3> verts = new();
        List<int> tris = new();

        void AddQuad(Vector3 bl, Vector3 br, Vector3 tl, Vector3 tr)
        {
            int index = verts.Count;

            verts.Add(bl);
            verts.Add(br);
            verts.Add(tl);
            verts.Add(tr);

            tris.Add(index + 0);
            tris.Add(index + 2);
            tris.Add(index + 1);

            tris.Add(index + 2);
            tris.Add(index + 3);
            tris.Add(index + 1);
        }

        float y0 = 0;
        float y1 = height;

        float d0 = doorStart;
        float d1 = doorEnd;
        float dh = doorHeight;

        // LEFT SECTION
        if (d0 > start)
        {
            AddSection(start, d0, y0, y1);
        }

        // RIGHT SECTION
        if (d1 < end)
        {
            AddSection(d1, end, y0, y1);
        }

        // TOP SECTION ABOVE DOOR
        AddSection(d0, d1, dh, y1);

        void AddSection(float s, float e, float bottom, float top)
        {
            Vector3 bl, br, tl, tr;

            if (runsAlongX)
            {
                bl = new Vector3(s, bottom, 0);
                br = new Vector3(e, bottom, 0);
                tl = new Vector3(s, top, 0);
                tr = new Vector3(e, top, 0);
            }
            else
            {
                bl = new Vector3(0, bottom, s);
                br = new Vector3(0, bottom, e);
                tl = new Vector3(0, top, s);
                tr = new Vector3(0, top, e);
            }

            AddQuad(bl, br, tl, tr);
        }

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
}