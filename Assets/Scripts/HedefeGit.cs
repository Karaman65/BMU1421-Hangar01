using UnityEngine;

[DisallowMultipleComponent]
public class HedefeGit : MonoBehaviour
{
    public Transform hedef;
    [Min(0f)] public float hiz = 14f;
    [Min(0f)] public float donusHizi = 60f;
    [Min(0f)] public float varisMesafesi = 8f;

    private const float MaksimumYatis = 35f;

    private void Update()
    {
        if (hedef == null) return;

        Vector3 hedefVektoru = hedef.position - transform.position;
        float mesafe = hedefVektoru.magnitude;
        float durmaMesafesi = Mathf.Max(0f, varisMesafesi);
        if (mesafe <= durmaMesafesi || mesafe < .0001f) return;

        Vector3 yon = hedefVektoru.normalized;
        // World up avoids feeding the current bank back into the turn direction.
        float donusYonu = Vector3.Dot(Vector3.Cross(transform.forward, yon), Vector3.up);
        float yatis = -donusYonu * MaksimumYatis;
        Quaternion hedefDonus = Quaternion.LookRotation(yon, Vector3.up)
            * Quaternion.AngleAxis(yatis, Vector3.forward);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, hedefDonus, Mathf.Max(0f, donusHizi) * Time.deltaTime);

        // Stop at the arrival radius instead of stepping through a nearby target.
        float adim = Mathf.Min(Mathf.Max(0f, hiz) * Time.deltaTime, mesafe - durmaMesafesi);
        transform.position += transform.forward * adim;
    }
}
