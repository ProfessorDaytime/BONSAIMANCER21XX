using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayMenuSounds : MonoBehaviour
{
    
    public void PlayStartSound(){
        AudioManager.Instance.ToggleMusic();
        AudioManager.Instance.PlaySFX("Start");
    }

    public void PlayTrimSound(){
        AudioManager.Instance.PlaySFX("Boop");
    }

    public void PlayWaterSound(){
        AudioManager.Instance.PlaySFX("Water");
    }

    
}
