// using System.Collections;
// using System.Collections.Generic;
// using UnityEngine;

// public class TreeGenPT002 : MonoBehaviour
// {
//     // Parameter variables
//     Vector3 lastPos;
//     float coneHeight = 2f;
//     float baseRadius = 1f;
//     float ratio = 0.88f;
//     public int numCones = 10;

//     [SerializeField]
//     GameObject conePrefab;

//     Transform parent;

//     // List to keep track of all cones
//     List<GameObject> cones = new List<GameObject>();

//     // Start is called before the first frame update
//     void Start()
//     {
//         lastPos = transform.position;
        

//         // Generate the first cone
//         GenerateCone(-1);

//         // Generate 4 more cones
//         for (int i = 0; i < numCones - 1; i++)
//         {
//             GenerateCone(i);
//         }
//     }

//     void GenerateCone(int d)
//     {
//         GameObject newCone = Instantiate (conePrefab) as GameObject;
//         cones.Add(newCone);

//         scr_Cyl_001 stemPiece = newCone.GetComponent<scr_Cyl_001>();
//         stemPiece.transform.position = lastPos;

//         ProceduralCone coneScript = newCone.GetComponent<ProceduralCone>();
//         // coneScript.Debug001(d);

//         // Set cone properties
//         coneScript.SetBaseRadius(baseRadius);
//         coneScript.SetTopRadius(baseRadius * ratio);
//         coneScript.SetHeight(coneHeight);

//         // Update variables for the next cone
//         lastPos += new Vector3(0f, coneHeight * 0.5f, 0f);
//         baseRadius *= ratio;
//         stemPiece.transform.SetParent(parent);
//         parent = stemPiece.transform;

        
//     }

//     // Function to check if any cones have been destroyed
//     bool ConesDestroyed()
//     {
//         for (int i = 0; i < cones.Count; i++)
//         {
//             if (cones[i] == null)
//             {
//                 return true;
//             }
//         }
//         return false;
//     }

//     // Update is called once per frame
//     void Update()
//     {
//         // Check if any cones have been destroyed
//         if (ConesDestroyed())
//         {
//             Debug.Log("Some cones have been destroyed.");
//             // Perform actions based on destroyed cones
//             // ...
//         }
//     }
// }
