using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class StartButton : MonoBehaviour
{
    [SerializeField] private Button _startButton;

    private void OnValidate()
    {
        _startButton ??= GetComponent<Button>();
    }

    private void OnEnable()
    {
        _startButton.onClick.AddListener(LoadGameScene);
    }
    private void OnDisable()
    {
        _startButton.onClick.RemoveListener(LoadGameScene);
    }

    private void LoadGameScene()
    {
        SceneManager.LoadScene("GameScene");
    }
}
