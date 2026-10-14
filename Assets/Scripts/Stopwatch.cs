using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class Stopwatch : MonoBehaviour
{
    public TMP_Text StopwatchText;
    public TMP_Text EndTimerText;
           // Drag your UI Text here in Inspector
    public TMP_Text bestTimeText;    // Drag your UI Text here in Inspector for best time display

    public float ElapsedTime => elapsedTime;
    private float elapsedTime = 0f;
    private bool isRunning = false;
    float currentTime = 0f;
    //bool timerRunning = false; // is taken care of by isRunning

    const string HighscoreKey = "BestTime";

    void Start()
    {
        isRunning = true;
        UpdateDisplay(); // shows 00:00.00 on game start
    }

    void Update()
    {
        if (isRunning)
        {
            elapsedTime += Time.deltaTime;
            UpdateDisplay();
        }
    }

    public void StartStop()
    {
        isRunning = !isRunning;
    }

    // Pause/Resume are explicit (unlike StartStop, which toggles), so the pause menu can't
    // flip the timer the wrong way. Resume only restarts it if it was running when paused.
    private bool isPaused = false;
    private bool wasRunningBeforePause = false;

    public void Pause()
    {
        if (isPaused) return;
        isPaused = true;
        wasRunningBeforePause = isRunning;
        isRunning = false;
    }

    public void Resume()
    {
        if (!isPaused) return;
        isPaused = false;
        isRunning = wasRunningBeforePause;
    }

    public void Reset()
    {
        isRunning = false;
        elapsedTime = 0f;
        UpdateDisplay();
    }

    public void UpdateDisplay()
    {
        int minutes = (int)(elapsedTime / 60);
        int seconds = (int)(elapsedTime % 60);
        int milliseconds = (int)((elapsedTime * 100) % 100);

        StopwatchText.text = string.Format("{0:00}:{1:00}.{2:00}", minutes, seconds, milliseconds);
    }

    public float GetTime()
    {
        
        return elapsedTime;
    }

        public void ShowEndTime()
    {
        EndTimerText.text = UIManager.instance.FormatTime(elapsedTime);
    }

    public void CheckForBestTime()
    {
        
        if(elapsedTime == 0f)
            return;
        else
        {
            int level = SceneManager.GetActiveScene().buildIndex;
            float BestTime = PlayerPrefs.GetFloat($"BestTime_Level{level}", float.MaxValue);

            if (elapsedTime < BestTime)
            {
                PlayerPrefs.SetFloat($"BestTime_Level{level}", elapsedTime);
                Debug.Log("New Best Time: " + elapsedTime);
                bestTimeText.text = "Best Time: " + UIManager.instance.FormatTime(elapsedTime);
            }
            else
            {
                bestTimeText.text = "Best Time: " + UIManager.instance.FormatTime(BestTime);
            }
        }
    }
    
}
