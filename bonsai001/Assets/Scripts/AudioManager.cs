using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;
    public Sound[] musicSounds, sfxSounds;
    public AudioSource musicSource, sfxSource; 

    void Awake()
    {
        if(Instance == null){
            Instance = this;
            DontDestroyOnLoad(gameObject);
        } else{
            // Destroy only this component, never the GameObject. In Game001 this component shares
            // its GameObject with GameManager and seven other managers; destroying the object
            // silently killed the whole simulation whenever a second AudioManager arrived from
            // another scene (2026-10-13). A duplicate should remove itself and nothing else.
            Debug.LogWarning($"[Audio] Duplicate AudioManager on '{gameObject.name}' — removing the " +
                             "duplicate component only.");
            Destroy(this);
        }

    }

    public void DestroyAudioManager(){
        Destroy(gameObject);
    }

    void Start()
    {
        PlayMusic("SpringSong");
    }
    public void PlayMusic(string name){
        Sound s = Array.Find(musicSounds, x=> x.name==name);

        if(s == null){
            Debug.LogWarning($"[Audio] PlayMusic: clip '{name}' not found in musicSounds array.");
        } else if(musicSource == null){
            Debug.LogWarning("[Audio] PlayMusic: musicSource is not assigned.");
        } else{
            musicSource.clip = s.clip;
            musicSource.Play();
            Debug.Log($"[Audio] Playing music: {name} | muted={musicSource.mute} volume={musicSource.volume:F2}");
        }
    }

    public void ToggleMusic(){
        musicSource.mute = !musicSource.mute;
    }

    /// <summary>The clip for a named SFX, or null if none is assigned (no log).</summary>
    public AudioClip FindSFX(string name) => sfxSounds == null ? null : Array.Find(sfxSounds, x => x.name == name)?.clip;

    public void PlaySFX(string name){
        Sound s = Array.Find(sfxSounds, x=> x.name==name);

        if(s == null){
            Debug.Log("Sound Effect Not Found");
        } else{
            sfxSource.PlayOneShot(s.clip);
        }
    }
}
