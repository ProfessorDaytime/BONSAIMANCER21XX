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

                // Debug.Log(hit);
                if (hit.transform.name == this.name && GameManager.canTrim) {
                    ProceduralCone coneScript = hit.transform.parent.GetComponent<ProceduralCone>();

                    AudioManager.Instance.PlaySFX("Trim");
                    // coneScript.GenerateCone();
                    // Debug.Log("BR " + coneScript.GetBaseRadius() + " TR " + coneScript.GetTopRadius() + " H " + coneScript.GetHeight());
                    
                    GameManager.canTrim = false;

                    Destroy(hit.transform.gameObject);

                }
            }
        }
    }
}
