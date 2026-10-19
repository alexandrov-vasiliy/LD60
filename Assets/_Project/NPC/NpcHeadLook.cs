using UnityEngine;

/// <summary>
/// Плавно направляет лицо в камеру игрока относительно мировой или локальной вертикали.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("NPC/Head Look")]
public sealed class NpcHeadLook : MonoBehaviour
{
    [SerializeField, Tooltip("Цель взгляда. Если не задана, используется камера с тегом MainCamera.")]
    private Transform target;
    [SerializeField, Tooltip("World — мировая вертикаль Y, Self — вертикаль исходного положения головы.")]
    private Space axisSpace = Space.World;
    [SerializeField, Tooltip("Куда смотрит лицо в локальных координатах меша. Например (0, 0, -1), если лицо направлено назад.")]
    private Vector3 localLookDirection = new Vector3(0, -1, 0);
    [SerializeField, Min(0f), Tooltip("Скорость поворота в градусах в секунду.")]
    private float rotationSpeed = 360f;

    private Quaternion initialLocalRotation;

    private void Awake() => initialLocalRotation = transform.localRotation;

    private void LateUpdate()
    {
        if (target == null)
        {
            var camera = Camera.main;
            if (camera == null) return;
            target = camera.transform;
        }

        Vector3 direction = target.position - transform.position;
        if (direction.sqrMagnitude < 0.0001f) return;

        Quaternion restRotation = (transform.parent != null ? transform.parent.rotation : Quaternion.identity)
            * initialLocalRotation;
        Vector3 localUp = Mathf.Abs(Vector3.Dot(localLookDirection.normalized, Vector3.up)) > 0.999f
            ? Vector3.forward : Vector3.up;
        Vector3 up = axisSpace == Space.World ? Vector3.up : restRotation * localUp;
        Quaternion lookOffset = Quaternion.Inverse(Quaternion.LookRotation(restRotation * localLookDirection, up))
            * restRotation;
        Quaternion desiredRotation = Quaternion.LookRotation(direction, up) * lookOffset;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, desiredRotation, rotationSpeed * Time.deltaTime);
    }

    private void OnValidate()
    {
        rotationSpeed = Mathf.Max(0f, rotationSpeed);
        if (localLookDirection.sqrMagnitude < 0.0001f)
            localLookDirection = Vector3.forward;
    }
}
