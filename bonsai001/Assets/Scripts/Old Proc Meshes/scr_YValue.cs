using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class scr_YValue : MonoBehaviour
{
    public float yValue;
    public static scr_YValue ins;

    void Awake(){
        ins = this;
    }
}
