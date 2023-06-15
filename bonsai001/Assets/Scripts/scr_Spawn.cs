using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System;
using UnityEngine;

public class scr_Spawn : MonoBehaviour
{

    //Make some parameter variables

    Vector3 lastPos;
    float heightOfPlatform;
    int curTreeLength = 0;
   

    [SerializeField]
    GameObject cyl;

    Transform parent;



    // Start is called before the first frame update
    void Start()
    {
        lastPos = this.transform.position;
        heightOfPlatform = this.transform.localScale.y;
        
    }

    // Update is called once per frame
    void Update()
    {
        if(curTreeLength < 4){
            Spawn();
        }
    }

    void Spawn(){
        GameObject _cyl = Instantiate (cyl) as GameObject;
        scr_Cyl_001 stemPiece = _cyl.GetComponent<scr_Cyl_001>();

        stemPiece.transform.position = lastPos;
        lastPos = lastPos + new Vector3 (0,1,0);
        curTreeLength++;

        stemPiece.transform.SetParent(parent);
        parent = stemPiece.transform;

    }
}
