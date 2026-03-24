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
            Destroy(gameObject);
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

    public void PlaySFX(string name){
        Sound s = Array.Find(sfxSounds, x=> x.name==name);

        if(s == null){
            Debug.Log("Sound Effect Not Found");
        } else{
            sfxSource.PlayOneShot(s.clip);
        }
    }
}
