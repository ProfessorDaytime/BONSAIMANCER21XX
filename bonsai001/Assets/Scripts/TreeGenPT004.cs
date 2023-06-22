// using System.Collections;
// using System.Collections.Generic;
// using UnityEngine;

// public class TreeGenPT004 : MonoBehaviour
// {
//     // Parameter variables
//     Vector3 lastPos;
//     float coneHeight = .5f;
//     float baseRadius = 0.25f;
//     float ratio = 0.2f;
//     float growthDuration = 1000f; // Time in seconds to generate the tree
//     float coneDuration = 10f;
//     float spawnHeight = 3f; // Height at which new cones are spawned
    
//     float elapsedTime = 0f;

//     [SerializeField]
//     GameObject conePrefab;

//     Transform parent;

//     // List to keep track of all cones
//     List<GameObject> cones = new List<GameObject>();

//     // Start is called before the first frame update
//     void Start()
//     {
//         lastPos = transform.position;

//         // Start the tree generation coroutine
//         StartCoroutine(GenerateTree());
//     }

//     IEnumerator GenerateTree()
//     {
//         // Generate the first cone
//         GenerateCone(-1);

        
//         while (elapsedTime < growthDuration)
//         {
//             yield return null; // Wait for the next frame

//             // Calculate the progress based on elapsed time
//             float progress = elapsedTime / coneDuration;

//             // Update cone properties gradually
//             float targetBaseRadius = Mathf.Lerp(0.25f, 1f, progress);
//             float targetTopRadius = Mathf.Lerp(0.05f, 0.88f, progress);
//             float targetHeight = Mathf.Lerp(0.5f, 4f, progress);

//             UpdateConeProperties(targetBaseRadius, targetTopRadius, targetHeight);

//             // Check if the topmost cone has reached the spawn height
//             if (cones.Count > 0)
//             {
//                 GameObject topCone = cones[cones.Count - 1];
//                 ProceduralCone topConeScript = topCone.GetComponent<ProceduralCone>();
//                 float topConeHeight = topConeScript.GetHeight();

//                 if ((topConeHeight - lastPos.y) >= spawnHeight)
//                 {
//                     GenerateCone(cones.Count); // Generate a new cone
//                 }
//             }

//             elapsedTime += Time.deltaTime;
//         }
//     }

//     void GenerateCone(int d)
//     {
//         GameObject newCone = Instantiate(conePrefab);
//         cones.Add(newCone);

//         scr_Cyl_001 stemPiece = newCone.GetComponent<scr_Cyl_001>();
//         stemPiece.transform.position = lastPos + new Vector3(0,coneHeight,0);

//         ProceduralCone coneScript = newCone.GetComponent<ProceduralCone>();
//         // coneScript.Debug001(d);

//         // Set cone properties to their initial values
//         coneScript.SetBaseRadius(baseRadius);
//         coneScript.SetTopRadius(baseRadius * ratio);
//         coneScript.SetHeight(coneHeight);

//         // Update variables for the next cone
//         lastPos += new Vector3(0f, coneHeight * 0.5f, 0f);
//         baseRadius *= ratio;
//         // coneHeight = (coneHeight == 4f) ? 2f : 4f; // Reset coneHeight if it reaches 4f
//         stemPiece.transform.SetParent(parent);
//         parent = stemPiece.transform;
//         elapsedTime = 0f;
//     }

//     void UpdateConeProperties(float targetBaseRadius, float targetTopRadius, float targetHeight)
//     {
//         foreach (GameObject cone in cones)
//         {
//             ProceduralCone coneScript = cone.GetComponent<ProceduralCone>();

//             // Update the cone properties gradually
//             float baseRadius = Mathf.Lerp(coneScript.GetBaseRadius(), targetBaseRadius, Time.deltaTime);
//             float topRadius = Mathf.Lerp(coneScript.GetTopRadius(), targetTopRadius, Time.deltaTime);
//             float height = Mathf.Lerp(coneScript.GetHeight(), targetHeight, Time.deltaTime);

//             coneScript.SetBaseRadius(baseRadius);
//             coneScript.SetTopRadius(topRadius);
//             coneScript.SetHeight(height);
//         }
//     }
// }
