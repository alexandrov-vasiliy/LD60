using UnityEngine;
using UnityEngine.UI;

public class StartButton : MonoBehaviour
{
    [SerializeField] private Button _startButton;
    [SerializeField] private IntroComic intro;

    private void OnValidate() => _startButton ??= GetComponent<Button>();
    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _startButton.onClick.AddListener(StartGame);
    }
    private void OnDisable() => _startButton.onClick.RemoveListener(StartGame);

    private void StartGame()
    {
        _startButton.interactable = false;
        intro.Begin();
    }
}
