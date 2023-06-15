using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class scr_Cyl_001 : MonoBehaviour
{
    void Start() {
        Debug.Log(this.name);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButtonDown(0)){
            RaycastHit hit;
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            

            if (Physics.Raycast(ray, out hit)) {

                Debug.Log(hit);
                if (hit.transform.name == this.name) {
                    Destroy(hit.transform.gameObject);

                }
            }
        }
    }
}
