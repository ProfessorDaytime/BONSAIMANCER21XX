using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TreeRoot : MonoBehaviour
{
    // Parameter variables
    public Vector3 initPos;
    
    // Cone settings
    // public float baseRadius = 0.25f; //radius for the bottom of the cone
    // public float topRadius = .05f; // radius for the top of the cone
    // public float height = 0.25f; // height of cone
    // public float trRatio = 0.2f;

    public float baseRadius = 25f; //radius for the bottom of the cone
    public float topRadius = 24f; // radius for the top of the cone
    public float height = 25f; // height of cone
    public float trRatio = 20f;

    [SerializeField]
    GameObject conePrefab;

    Transform parent;

    // Start is called before the first frame update
    void Start()
    {
        initPos = new Vector3(0,0,0);
        GenerateCone();
    }

    void GenerateCone()
    {
        GameObject newCone = Instantiate(conePrefab);
        

        scr_Cyl_001 stemPiece = newCone.GetComponent<scr_Cyl_001>();
        stemPiece.transform.position = this.transform.position + new Vector3(0,1,0);

        ProceduralCone coneScript = newCone.GetComponent<ProceduralCone>();
        coneScript.treeSpot = 0;
        coneScript.treeBranch = 0;

        // Set cone properties to their initial values
        // coneScript.SetConeProperties(baseRadius,topRadius, height, 4f, 10f, 12f);
        coneScript.SetConeProperties(baseRadius,baseRadius, 1f, 1.0f, 0.88f, 1f);


        stemPiece.transform.SetParent(this.transform);


    }
    
}
