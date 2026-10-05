using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class UcakKontrol : MonoBehaviour
{
    [SerializeField, Min(0f)] private float normalHiz = 14f;
    [SerializeField, Min(0f)] private float hizliHiz = 28f;
    [SerializeField, Min(0f)] private float donusHizi = 70f;
    [SerializeField, Min(0f)] private float pitchHizi = 50f;
    [SerializeField, Min(0f)] private float rollHizi = 70f;

    private void Update()
    {
        var klavye = Keyboard.current;
        if (!isActiveAndEnabled || klavye == null) return;

        // Opposing keys cancel. No W/S input means no forward/backward movement.
        float hareket = (klavye.wKey.isPressed ? 1f : 0f) - (klavye.sKey.isPressed ? 1f : 0f);
        float yaw = (klavye.dKey.isPressed ? 1f : 0f) - (klavye.aKey.isPressed ? 1f : 0f);
        // Push forward (Up): nose down. Pull back (Down): nose up.
        float pitch = (klavye.upArrowKey.isPressed ? 1f : 0f) - (klavye.downArrowKey.isPressed ? 1f : 0f);
        // Positive local Z banks left; negative local Z banks right.
        float roll = (klavye.qKey.isPressed ? 1f : 0f) - (klavye.eKey.isPressed ? 1f : 0f);

        float dt = Time.deltaTime;
        transform.Rotate(pitch * pitchHizi * dt, yaw * donusHizi * dt,
            roll * rollHizi * dt, Space.Self);

        float hiz = klavye.leftShiftKey.isPressed ? hizliHiz : normalHiz;
        transform.position += transform.forward * (hareket * hiz * dt);
    }
}
