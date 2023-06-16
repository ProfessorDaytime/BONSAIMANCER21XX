using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System;
using UnityEngine;

public class TreeGen : MonoBehaviour
{

    //Make some parameter variables
    Vector3 lastPos;
    float heightOfPlatform;
    int curTreeLength = 0;
    int initLength = 4;
    
   

    [SerializeField]
    GameObject cone;

    Transform parent;



    // Start is called before the first frame update
    void Start()
    {
        lastPos = this.transform.position;
        heightOfPlatform = this.transform.localScale.y;

        for (int i = 0; i < initLength; i++){
            Gen();
        }
        
    }

    

    void Gen(){
        GameObject _cone = Instantiate (cone) as GameObject;
        scr_Cyl_001 stemPiece = _cone.GetComponent<scr_Cyl_001>();

        stemPiece.transform.position = lastPos;
        lastPos = lastPos + new Vector3 (0,1,0);
        curTreeLength++;

        stemPiece.transform.SetParent(parent);
        parent = stemPiece.transform;

    }
}
