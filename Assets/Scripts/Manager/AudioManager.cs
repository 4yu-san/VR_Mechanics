using UnityEngine;

public class AudioManager : MonoBehaviour
{
    // Singleton instance
    private static AudioManager _instance;
    public static AudioManager instance
    {
        get
        {
            if (_instance == null)
                _instance = UnityEngine.Object.FindAnyObjectByType<AudioManager>();
            return _instance;
        }
    }

    [Header("Audio Sources")]
    public AudioSource musicSource;
    public AudioSource sfxSource;

    [Header("Audio Clips")]
    public AudioClip menuMusic;
    public AudioClip gameMusic;
    public AudioClip levelCompleteSound;
    public AudioClip gameOverSound;
    public AudioClip grappleSound;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // --- Music ---
    public void PlayMenuMusic()
    {
        PlayMusic(menuMusic);
    }

    public void PlayGameMusic()
    {
        PlayMusic(gameMusic);
    }

    private void PlayMusic(AudioClip clip)
    {
        if (musicSource.clip == clip) return; // Already playing
        musicSource.clip = clip;
        musicSource.loop = true;
        musicSource.Play();
    }

    public void StopMusic()
    {
        musicSource.Stop();
    }

    // --- SFX ---
    public void PlayLevelComplete()
    {
        sfxSource.PlayOneShot(levelCompleteSound);
    }

    public void PlayGameOver()
    {
        sfxSource.PlayOneShot(gameOverSound);
    }

    public void PlaySFX(AudioClip clip)
    {
        sfxSource.PlayOneShot(clip);
    }

    // --- Volume ---
    public void SetMusicVolume(float volume)
    {
        musicSource.volume = volume;
    }

    public void SetSFXVolume(float volume)
    {
        sfxSource.volume = volume;
    }
}