using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer),typeof(MeshCollider))]
public class ProceduralCone : MonoBehaviour
{
    Mesh mesh; //mesh object for cone
    MeshCollider coll; //gives the cone a collider
    Vector3[] vertices; //an array of vertices for the cone
    int[] triangles; //an array of triangles that connect those vertices to create faces
    Vector3[] normals; //an array of the normals for the vertices

    // Grid settings
    public float cellSize = 1; //could be used for scaling, but doesn't really seem like it's useful
    public Vector3 gridOffset; //this can move things around, but the parent transform might be more useful
    public int gridSizeX = 16; //face count of polygon shape
    public int gridSizeY = 4; //vertical face loops
    public int treeSpot;
    public int treeBranch;
    public bool growState = false;

    // Cone settings
    float baseRadius;// = .25f; //radius for the bottom of the cone
    float topRadius;// = 0.05f; // radius for the top of the cone
    float height;// = 0.25f; // height of cone
    // float trRatio = 0.2f; // TopRadius ratio for the start of the cone

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
    bool cRunning = false;

    //It will reference a prefab of itself to create more.  I hope this is the right way to do it
    [SerializeField]
    GameObject conePrefab;
    [SerializeField]
    GameObject conePrefab2;

    void Awake()
    {
        MakeProceduralCone();
        // create the mesh filter
        // this.mesh = this.GetComponent<MeshFilter>().mesh;
        
        GameManager.OnGameStateChanged += GameManagerOnGameStateChanged;

        Debug.Log("Awake");

        if(growState){
            //Start the child generation coroutine
            StartCoroutine(Grow());
        }
        
        
    }

    void OnDestroy()
    {
        GameManager.OnGameStateChanged -= GameManagerOnGameStateChanged;
    }


    private void GameManagerOnGameStateChanged(GameState state){
        //Set grow tree bool
        if(state == GameState.BranchGrow){
            growState = true;
            //Start the child generation coroutine
            StartCoroutine(Grow());
            cRunning = true;
        } else{
            growState = false;
            if(cRunning){
                StopCoroutine(Grow());
                cRunning = false;
            }
        }

    }

    void CreateMesh(){
        mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;

        // create the mesh collider
        coll = this.GetComponent<MeshCollider>();
        coll.sharedMesh = null; //nulls out the mesh for the mesh collider so it isn't fucky
        coll.sharedMesh = mesh; //assigns the mesh as the collider's shared mesh
    }

    IEnumerator Grow(){

        while (!doneGrowing && growState){
            yield return null; // Wait for the next frame

            // Calculate the progress based on elapsed time
            float progress = elapsedTime / coneDuration;

            // Update cone properties gradually
            float br = Mathf.Lerp(initBR, targetBaseRadius, progress);
            float tr = Mathf.Lerp(initTR, targetTopRadius, progress);
            float h = Mathf.Lerp(initH, targetHeight, progress);

            // Debug.Log("progress: " + progress + " br: " + br + " tr: " + tr + " h: " + h + " position: " + this.transform.position + " Target BR: " + targetBaseRadius + " Target TR: " + targetTopRadius + " Target Height:" + targetHeight);

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

        // Debug.Log("SCP BR: " + br + " TR: " + tr + " H " + h);

        SetTargetConeProperties();        
        MakeProceduralCone();
        UpdateMesh();
    }

    void SetTargetConeProperties(){
        targetBaseRadius = baseRadius * growBR;
        targetTopRadius = topRadius * growTR;
        targetHeight = 100f;
        // Debug.Log("STargetCP TBR: " + targetBaseRadius + " TTR: " + targetTopRadius + " TH: " + targetHeight);
    }


    void GenUpAndBranch(){
        GameObject newCone = Instantiate(conePrefab);
        GameObject newCone2 = Instantiate(conePrefab2);//, this.transform.position + this.transform.up, this.transform.rotation, this.transform);
        
        float yRot = Random.Range(0,360);
        float xRot = Random.Range(15, 45);
        float zRot = Random.Range(15, 45);
        
    
        scr_Cyl_001 stemPiece = newCone.GetComponent<scr_Cyl_001>();
        scr_Cyl_001 stemPiece2 = newCone2.GetComponent<scr_Cyl_001>();
        stemPiece.transform.position = this.transform.position + (this.transform.up * height * 0.5f);
        stemPiece2.transform.position =  this.transform.position + (this.transform.up * height * 0.5f);
        stemPiece2.transform.eulerAngles = new Vector3(xRot,yRot,zRot);

        ProceduralCone coneScript = newCone.GetComponent<ProceduralCone>();
        ProceduralCone coneScript2 = newCone2.GetComponent<ProceduralCone>();

        // Set cone properties to their initial values
        coneScript.SetConeProperties(topRadius,topRadius, 1f, 1.0f, 0.88f, 2.0f);
        coneScript2.SetConeProperties(topRadius * .5f,topRadius * .5f, 1f, 1.0f, 0.88f, 1.0f);
        coneScript.treeSpot = this.treeSpot + 1;
        coneScript.treeBranch = this.treeBranch;
        coneScript2.treeSpot = this.treeSpot + 1;
        coneScript2.treeBranch = this.treeBranch + 1;
        coneScript.growState = true;
        coneScript2.growState = true;

        stemPiece.transform.SetParent(this.transform);
        stemPiece2.transform.SetParent(this.transform);
    }

    void GenUp(){
        GameObject newCone = Instantiate(conePrefab);
            
        
        scr_Cyl_001 stemPiece = newCone.GetComponent<scr_Cyl_001>();
        stemPiece.transform.position = this.transform.position + (this.transform.up * height * 0.5f);

        ProceduralCone coneScript = newCone.GetComponent<ProceduralCone>();

        // Set cone properties to their initial values
        coneScript.SetConeProperties(topRadius,topRadius, 1f, 1.0f, 0.88f, 2.0f);
        coneScript.treeSpot = this.treeSpot + 1;
        coneScript.treeBranch = this.treeBranch;
        coneScript.growState = true;

        stemPiece.transform.SetParent(this.transform);
    }




    public void GenerateCone()
    {

        int branch = Random.Range(0,10);
        
        if(this.treeSpot == 0 && this.treeBranch == 0){
            GenUpAndBranch();
        }
        
        
        else if(branch >= 7){
            GenUpAndBranch();
        } 
        
        else{
            GenUp();
        }

    }
    
   
    // public void SetBaseRadius(float br){
    //     baseRadius = br;
    //     // Debug.Log("br: " + br);
    //     MakeProceduralCone();
    //     UpdateMesh();
    // }

    // public float GetBaseRadius(){
    //     return baseRadius;
    // }

    // public void SetTopRadius(float tr){
    //     topRadius = tr;
    //     // Debug.Log("tr: " + tr);
    //     MakeProceduralCone();
    //     UpdateMesh();
    // }

    // public float GetTopRadius(){
    //     return topRadius;
    // }

    // public void SetHeight(float h){
    //     height = h;
    //     // Debug.Log("h: " + h);
    //     MakeProceduralCone();
    //     UpdateMesh();
    // }

    // public float GetHeight(){
    //     return height;
    // }


    
    void MakeProceduralCone(){

        CreateMesh();

        // Set array sizes
        this.vertices = new Vector3[(gridSizeX + 1) * (gridSizeY + 1)];
        this.triangles = new int[gridSizeX * gridSizeY * 6];
        this.normals = new Vector3[vertices.Length];

        // Set tracker integers
        int v = 0; //v is for vertices
        int t = 0; //t is for triangles


        // Debug.Log("MakeProceduralCone:   branch: " + treeBranch + " spot: " + treeSpot + " vertices: " + this.vertices.Length);

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



    void UpdateMesh(){
        if (mesh == null || vertices == null || vertices.Length < 3 || triangles == null || normals == null)
            return;

        Debug.Log("UpdateMesh:   branch: " + treeBranch + " spot: " + treeSpot + " vertices: " + this.vertices.Length);


        mesh.Clear();

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.normals = normals;
        coll.sharedMesh = null;
        coll.sharedMesh = mesh;
    }





}
