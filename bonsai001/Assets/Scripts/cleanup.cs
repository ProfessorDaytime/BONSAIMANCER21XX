using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer),typeof(MeshCollider))]
public class cleanup : MonoBehaviour
{
    Mesh mesh; //mesh object for cone
    MeshCollider coll; //gives the cone a collider
    Vector3[] vertices; //an array of vertices for the cone
    int[] triangles; //an array of triangles that connect those vertices to create faces
    Vector3[] normals; //an array of the normals for the vertices

    // Grid settings
    public float cellSize = 1; //could be used for scaling, but doesn't really seem like it's useful
    public Vector3 gridOffset; //this can move things around, but the parent transform might be more useful
    public int gridSizeX = 24; //face count of polygon shape
    public int gridSizeY = 3; //vertical face loops

    // Cone settings
    float baseRadius;// = .25f; //radius for the bottom of the cone
    float topRadius;// = 0.05f; // radius for the top of the cone
    float height;// = 0.25f; // height of cone


    // initial Cone Settings
    float initBR;
    float initTR;
    float initH;

    // Settings for making children
    float targetBaseRadius;
    float targetTopRadius;
    float targetHeight;

    //Rate for growth of the Base Radius, Top Radius, and Height
    float growBR = 4f;
    float growTR = 17.6f;
    float growH = 4f;


    // float growthDuration = 1000f; // Time in seconds to generate the tree
    float coneDuration = 4f; // how fast a single cone grows
    float elapsedTime = 0f;
    bool doneGrowing = false;

    //It will reference a prefab of itself to create more.  I hope this is the right way to do it
    [SerializeField]
    GameObject conePrefab;


    void Awake()
    {
        // create the mesh filter
        mesh = GetComponent<MeshFilter>().mesh;

        // create the mesh collider
        coll = GetComponent<MeshCollider>();
        coll.sharedMesh = null; //nulls out the mesh for the mesh collider so it isn't fucky
        coll.sharedMesh = mesh; //assigns the mesh as the collider's shared mesh

        //Start the child generation coroutine
        StartCoroutine(Grow());
    }

    IEnumerator Grow(){

        while (!doneGrowing){
            yield return null; // Wait for the next frame

            // Calculate the progress based on elapsed time
            float progress = elapsedTime / coneDuration;

            // Update cone properties gradually
            float br = Mathf.Lerp(initBR, targetBaseRadius, progress);
            float tr = Mathf.Lerp(initTR, targetTopRadius, progress);
            float h = Mathf.Lerp(initH, targetHeight, progress);

            Debug.Log("progress: " + progress + " br: " + br + " tr: " + tr + " h: " + h + " position: " + this.transform.position + " Target BR: " + targetBaseRadius + " Target TR: " + targetTopRadius + " Target Height:" + targetHeight);

            UpdateConeProperties(br, tr, h);

            if (h >= targetHeight){
                Debug.Log("DONE GROWING");
                doneGrowing = true;
                //Generate a child cone
                GenerateCone();
            }

            elapsedTime += Time.deltaTime;
        }
    }


    void UpdateConeProperties(float br, float tr, float h){       
        baseRadius = br;
        topRadius = tr;
        height = h;

        MakeProceduralCone();
        UpdateMesh();
    }

    public void SetConeProperties(float br, float tr, float h, float gBR, float gTR, float gH){
        baseRadius = br;
        topRadius = tr;
        height = h;

        initBR = br;
        initTR = tr;
        initH = h;

        doneGrowing = false;
        elapsedTime = 0f; 

        growBR = gBR;
        growTR = gTR;
        growH = gH;

        Debug.Log("SCP BR: " + br + " TR: " + tr + " H " + h);

        SetTargetConeProperties();
        
        MakeProceduralCone();
        UpdateMesh();
    }

    void SetTargetConeProperties(){
        targetBaseRadius = baseRadius * growBR;
        targetTopRadius = topRadius * growTR;
        targetHeight = 100f;
        Debug.Log("STargetCP TBR: " + targetBaseRadius + " TTR: " + targetTopRadius + " TH: " + targetHeight);
    }

    public void GenerateCone()
    {
        GameObject newCone = Instantiate(conePrefab);//, this.transform.position + this.transform.up, this.transform.rotation, this.transform);

        scr_Cyl_001 stemPiece = newCone.GetComponent<scr_Cyl_001>();
        stemPiece.transform.position =  this.transform.position + (this.transform.up * height * 0.5f);
        Debug.Log("transform.up: " + this.transform.up);
        
        ProceduralCone coneScript = newCone.GetComponent<ProceduralCone>();

        // Set cone properties to their initial values
        Debug.Log("New BR: " + topRadius + " new TR: " + topRadius);
        coneScript.SetConeProperties(topRadius,topRadius, 1f, 1.0f, 0.88f, 4.0f);

        stemPiece.transform.SetParent(this.transform);
    }
    
    void MakeProceduralCone(){
        // Set array sizes
        vertices = new Vector3[(gridSizeX + 1) * (gridSizeY + 1)];
        triangles = new int[gridSizeX * gridSizeY * 6];
        normals = new Vector3[vertices.Length];

        // Set tracker integers
        int v = 0; //v is for vertices
        int t = 0; //t is for triangles

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
                Vector3 vertexPosition = (new Vector3(xPos, yPos, zPos) * vertexOffset) + gridOffset;
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
        if (mesh == null || vertices == null || triangles == null || normals == null)
            return;

        mesh.Clear(false);

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.normals = normals;
        coll.sharedMesh = null;
        coll.sharedMesh = mesh;
    }
}
