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
            Debug.Log("Music Not Found");
        } else{
            musicSource.clip = s.clip;
            musicSource.Play();
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
