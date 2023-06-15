using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer),typeof(MeshCollider))]
public class ProceduralCone : MonoBehaviour
{
    Mesh mesh;
    MeshCollider coll;
    Vector3[] vertices;
    int[] triangles;
    Vector3[] normals;

    // Grid settings
    public float cellSize = 1;
    public Vector3 gridOffset;
    public int gridSizeX = 3;
    public int gridSizeY = 3;

    // Cone settings
    public float baseRadius = 2f;
    public float topRadius = 1f;
    public float height = 3f;

    void Awake()
    {
        mesh = GetComponent<MeshFilter>().mesh;
        
        coll = GetComponent<MeshCollider>();
        coll.sharedMesh = null;
        coll.sharedMesh = mesh;
    }

    void Update()
    {
        MakeProceduralCone();
        UpdateMesh();
    }

    void MakeProceduralCone(){
        // Set array sizes
        vertices = new Vector3[(gridSizeX + 1) * (gridSizeY + 1)];
        triangles = new int[gridSizeX * gridSizeY * 6];
        normals = new Vector3[vertices.Length];

        // Set tracker integers
        int v = 0;
        int t = 0;

        // Set vertex offset
        float vertexOffset = cellSize * 0.5f;

        // Create vertex grid
        for (int x = 0; x <= gridSizeX; x++)
        {
            for (int y = 0; y <= gridSizeY; y++)
            {
                float progress = (float)y / gridSizeY; // Calculate the progress from 0 to 1
                float curRadius = Mathf.Lerp(baseRadius, topRadius, progress); // Interpolate between the base and top radius

                float angle = (float)x / gridSizeX * 2f * Mathf.PI; // Calculate the angle around the cone

                // Calculate the vertex position using cylindrical coordinates
                float yPos = progress * height;
                float xPos = curRadius * Mathf.Sin(angle);
                float zPos = curRadius * Mathf.Cos(angle);

                // Calculate the outward-pointing normal vector
                Vector3 vertexPosition = new Vector3(xPos, yPos, zPos) + gridOffset;
                Vector3 vertexNormal = (vertexPosition - gridOffset).normalized * -1;
                vertices[v] = vertexPosition;
                normals[v] = vertexNormal;
                v++;
            }
        }

        // Reset vertex tracker
        v = 0;

        // Set each cell's triangles
        for (int x = 0; x < gridSizeX; x++)
        {
            for (int y = 0; y < gridSizeY; y++)
            {
                // triangles[t] = v;
                // triangles[t + 1] = triangles[t + 4] = v + 1;
                // triangles[t + 2] = triangles[t + 3] = v + (gridSizeY + 1);
                // triangles[t + 5] = v + (gridSizeY + 1) + 1;

                triangles[t] = v;
                triangles[t + 1] = v + (gridSizeY + 1);
                triangles[t + 2] = v + (gridSizeY + 1) + 1;

                triangles[t + 3] = v + 1;
                triangles[t + 4] = v;
                triangles[t + 5] = v + (gridSizeY + 1) + 1;
                

                v++;
                t += 6;
            }
            v++;
        }
    }

    void UpdateMesh()
    {
        mesh.Clear();

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.normals = normals;
        coll.sharedMesh = null;
        coll.sharedMesh = mesh;
    }
}
