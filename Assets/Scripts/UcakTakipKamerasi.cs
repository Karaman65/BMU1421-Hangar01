using UnityEngine;

[DisallowMultipleComponent]
public class UcakTakipKamerasi : MonoBehaviour
{
    [SerializeField] private Transform hedef;

    [SerializeField, Min(0f)] private float distance = 13f;
    [SerializeField] private float height = 4.5f;
    [SerializeField] private float lookAhead = 2.5f;
    [SerializeField, Min(0.001f)] private float pozisyonSmoothTime = 0.25f;
    [SerializeField, Min(0f)] private float donusYumusakligi = 2.5f;
    private Vector3 hizVelocity;

    private void OnEnable()
    {
        hizVelocity = Vector3.zero;
        if (!Application.isPlaying) return;
        if (TryGetComponent<Camera>(out var kamera))
            kamera.fieldOfView = 65f;
        HedeftenAyir();
    }

    private void LateUpdate()
    {
        if (hedef == null) return;

        // Position follows yaw only: pitch and roll do not rotate the offset.
        Vector3 yatayForward = hedef.forward;
        yatayForward.y = 0f;
        if (yatayForward.sqrMagnitude < 0.001f)
            yatayForward = Vector3.forward;
        yatayForward.Normalize();

        Vector3 istenenKonum = hedef.position
            - yatayForward * distance + Vector3.up * height;
        transform.position = Vector3.SmoothDamp(
            transform.position,
            istenenKonum,
            ref hizVelocity,
            pozisyonSmoothTime
        );

        Vector3 bakisNoktasi = hedef.position
            + yatayForward * lookAhead + Vector3.up * 1.2f;
        Vector3 bakisYonu = bakisNoktasi - transform.position;
        if (bakisYonu.sqrMagnitude > 0.001f)
        {
            Quaternion istenenDonus = Quaternion.LookRotation(bakisYonu.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                istenenDonus,
                1f - Mathf.Exp(-donusYumusakligi * Time.deltaTime)
            );
        }
    }

    public void HedefAta(Transform yeniHedef)
    {
        hedef = yeniHedef;
        hizVelocity = Vector3.zero;
        // Editor setup handles parenting through Undo; runtime also stays independent.
        if (Application.isPlaying)
            HedeftenAyir();
    }

    private void HedeftenAyir()
    {
        if (hedef != null && transform != hedef && transform.IsChildOf(hedef))
            transform.SetParent(null, true);
    }
}
