using UnityEngine;

[DefaultExecutionOrder(100)]
public class Main : MonoBehaviour
{
    private void Start()
    {
        Application.targetFrameRate = 60;
        Cursor.lockState = CursorLockMode.Locked;
        
    }
    
}
