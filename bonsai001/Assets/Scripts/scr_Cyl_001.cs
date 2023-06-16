using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class scr_Cyl_001 : MonoBehaviour
{
    void Start() {
        
    }

    // Update is called once per frame
    void Update()
    {
        CheckClick();
    }

    void CheckClick(){
        if (Input.GetMouseButtonDown(0)){
            RaycastHit hit;
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            

            if (Physics.Raycast(ray, out hit)) {

                Debug.Log(hit);
                if (hit.transform.name == this.name) {
                    ProceduralCone coneScript = hit.transform.GetComponent<ProceduralCone>();
                    Debug.Log("BR " + coneScript.GetBaseRadius() + " TR " + coneScript.GetTopRadius() + " H " + coneScript.GetHeight());
                    Destroy(hit.transform.gameObject);

                }
            }
        }
    }
}
