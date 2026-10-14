using UnityEngine;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    
    public bool gameOver = false;
    public Stopwatch stopwatch;

    public bool hasGameStarted = false;
    public float finalTime = 0f;
    public GameState State { get; private set; } = GameState.Playing;

    public static GameManager _instance;

    public static GameManager instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = UnityEngine.Object.FindAnyObjectByType<GameManager>();
            }

            return _instance;
        }
    }

    public enum GameState
    {
        Playing,
        GameOver,
        Paused
    }

    public void SetState(GameState newState)
{
    State = newState;

    switch (newState)
    {
        case GameState.Playing:
            Time.timeScale = 1f;
            hasGameStarted = true;
            gameOver = false;
            if (stopwatch != null) stopwatch.Resume();
            break;
        case GameState.GameOver:
            Time.timeScale = 0f;
            gameOver = true;
            break;
        case GameState.Paused:
            Time.timeScale = 0f;
            if (stopwatch != null) stopwatch.Pause();
            break;
    }
}

    //Function LIst
    public void StartGame()
    {
        hasGameStarted = true;
            State = GameState.Playing;
            AudioManager.instance.PlayGameMusic();
            stopwatch.StartStop();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
    }
    public void GameOver()
    {
        gameOver = true;
        State = GameState.GameOver;
        Debug.Log("Game Over Triggered");
        //AudioManager.instance.PlayGameOver();
        stopwatch.StartStop();
        UIManager.instance.HandleGameOverUI();
        Time.timeScale = 0f; // Freeze game time
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void LevelCompleted()
    {
        State = GameState.GameOver;
        finalTime = stopwatch.ElapsedTime; // record it BEFORE stopping
        stopwatch.StartStop(); // now stop it
        stopwatch.ShowEndTime(); // show end time on level complete screen
        stopwatch.CheckForBestTime(); // check for best time after stopping
        UIManager.instance.HandleLevelCompleteUI();
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ResetGame()
    {
        gameOver = false;
        hasGameStarted = false;
        State = GameState.Playing;
        //AudioManager.instance.PlayGameMusic();
        stopwatch.Reset();
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }


}
