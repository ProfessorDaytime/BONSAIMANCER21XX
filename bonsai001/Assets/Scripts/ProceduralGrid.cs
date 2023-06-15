using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent (typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralGrid : MonoBehaviour
{

    Mesh mesh;
    Vector3[] vertices;
    int[] triangles;

    //grid settings
    public float cellSize = 1;
    public Vector3 gridOffset;
    public int gridSizeX = 3; //I think this will be the number of vertical edges or segments
    public int gridSizeY = 3; //This should end up being the height

    //cylinder settings
    public float baseRadius = 2f;
    public float topRadius = 1f;
    public float angle = 360f;
    public float angleAmount = 0f;
    


    
    void Awake(){
        mesh = GetComponent<MeshFilter> ().mesh;
    }

    // Start is called before the first frame update
    void Start(){
        MakeContiguousProceduralGrid();
        UpdateMesh();
    }

    void SetAngleAmount(){
        angleAmount = 2 * Mathf.PI / gridSizeX;
    }

    

    void MakeContiguousProceduralGrid(){
        //set array sizes
        vertices = new Vector3[(gridSizeX + 1) * (gridSizeY + 1)];
        triangles = new int[gridSizeX * gridSizeY * 6];

        //set tracker integers
        int v = 0;
        int t = 0;

        //set vertex offset
        float vertexOffset = cellSize * 0.5f;

        

        //create vertex grid
        for (int x = 0; x <= gridSizeX; x++){
            for (int y = 0; y <= gridSizeY; y++){


                //set curve
                float curRadius = baseRadius - ((y / gridSizeY) * topRadius);
                float curveX = curRadius * Mathf.Sin(angle);
                float curveZ = curRadius * Mathf.Cos(angle);

                vertices[v] = new Vector3((x * cellSize) - vertexOffset, 0, (y * cellSize) - vertexOffset);
                v++;
                angle -= angleAmount;
            }
        }

        //reset vertex tracker
        v = 0;

        //setting each cell's triangles
        for (int x = 0; x < gridSizeX; x++){
            for (int y = 0; y < gridSizeY; y++){
                triangles[t] = v;
                triangles[t + 1] = triangles[t + 4] = v + 1;
                triangles[t + 2] = triangles[t + 3] = v + (gridSizeY + 1);
                triangles[t + 5] = v + (gridSizeY + 1) + 1;

                v++;
                t += 6;

            }
            v++;
        }
        

    }

    void MakeDiscreteProceduralGrid(){
        //set array sizes
        vertices = new Vector3[gridSizeX * gridSizeY * 4];
        triangles = new int[gridSizeX * gridSizeY * 6];

        //set tracker integers
        int v = 0;
        int t = 0;

        //set vertex offset
        float vertexOffset = cellSize * 0.5f;

        for (int x = 0; x < gridSizeX; x++){
            for (int y = 0; y < gridSizeY; y++){
                Vector3 cellOffset = new Vector3(x * cellSize, 0, y * cellSize);


                //populate the vertices and triangles arrays
                vertices[v] = new Vector3(-vertexOffset,0,-vertexOffset) + cellOffset + gridOffset;
                vertices[v + 1] = new Vector3(-vertexOffset,0,vertexOffset) + cellOffset + gridOffset;
                vertices[v + 2] = new Vector3(vertexOffset,0,-vertexOffset) + cellOffset + gridOffset;
                vertices[v + 3] = new Vector3(vertexOffset,0,vertexOffset) + cellOffset + gridOffset;

                triangles[t] = v;
                triangles[t + 1] = triangles[t + 4] = v + 1;
                triangles[t + 2] = triangles[t + 3] = v + 2;
                triangles[t + 5] = v + 3;

                v += 4;
                t += 6;

            }
        }

        

    }


    void UpdateMesh(){
        mesh.Clear();

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
    }
}