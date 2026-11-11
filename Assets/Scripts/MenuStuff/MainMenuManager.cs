using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class MainMenuManager : MonoBehaviour
{
    [Header("Scene Navigation")]
    [Tooltip("Exact name of the main gameplay scene in Build Settings.")]
    [SerializeField] private string mainSceneName = "GameScene";

    [Header("UI Panels")]
    [SerializeField] private GameObject optionsPanel;
    [Tooltip("Optional: Assign the main buttons container to hide it while Options is open.")]
    [SerializeField] private GameObject mainButtonsPanel;

    private void Start()
    {
        // Ensure the options menu is hidden and time flows normally when entering the menu
        Time.timeScale = 1f;
        SetOptionsState(false);
    }

    private void Update()
    {
        // Close the options panel when pressing Escape using the new Input System
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (optionsPanel != null && optionsPanel.activeSelf)
            {
                CloseOptions();
            }
        }
    }

    public void PlayGame()
    {
        if (string.IsNullOrEmpty(mainSceneName))
        {
            Debug.LogError("[MainMenuManager] Main scene name is not set in the Inspector!");
            return;
        }

        SceneManager.LoadScene(mainSceneName);
    }

    public void OpenOptions()
    {
        SetOptionsState(true);
    }

    public void CloseOptions()
    {
        SetOptionsState(false);
    }

    public void ToggleOptions()
    {
        if (optionsPanel != null)
        {
            SetOptionsState(!optionsPanel.activeSelf);
        }
    }

    private void SetOptionsState(bool isOpen)
    {
        if (optionsPanel != null)
        {
            optionsPanel.SetActive(isOpen);
        }

        if (mainButtonsPanel != null)
        {
            mainButtonsPanel.SetActive(!isOpen);
        }
    }

    public void QuitGame()
    {
        Debug.Log("[MainMenuManager] Exiting game...");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}