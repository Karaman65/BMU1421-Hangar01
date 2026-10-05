using UnityEngine;

[DisallowMultipleComponent]
public class Radar : MonoBehaviour
{
    public Transform hedef;
    [Range(0f, 180f)] public float yarimAci = 35f;
    [Min(0f)] public float menzil = 120f;

    public bool Goruyor { get; private set; }
    // Positive: right, negative: left, zero: no lateral offset (or no target).
    public float YanTaraf { get; private set; }

    private void Update()
    {
        Goruyor = HedefiKontrolEt(out float yanTaraf);
        YanTaraf = yanTaraf;
    }

    private bool HedefiKontrolEt(out float yanTaraf)
    {
        yanTaraf = 0f;
        if (hedef == null) return false;

        Vector3 hedefVektoru = hedef.position - transform.position;
        float mesafeKare = hedefVektoru.sqrMagnitude;
        // At the radar origin there is no direction to normalize.
        if (mesafeKare == 0f) return true;

        Vector3 yon = hedefVektoru.normalized;
        yanTaraf = Vector3.Dot(Vector3.Cross(transform.forward, yon), transform.up);
        float sinir = Mathf.Cos(Mathf.Clamp(yarimAci, 0f, 180f) * Mathf.Deg2Rad);
        float gorusDot = Vector3.Dot(transform.forward, yon);
        float etkinMenzil = Mathf.Max(0f, menzil);
        // Geometric cone only: walls/occlusion are not tested.
        return mesafeKare <= etkinMenzil * etkinMenzil && gorusDot >= sinir;
    }

    private void OnDisable()
    {
        Goruyor = false;
        YanTaraf = 0f;
    }

    private void OnDrawGizmos()
    {
        Color oncekiRenk = Gizmos.color;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position,
            transform.position + transform.forward * Mathf.Max(0f, menzil));

        if (hedef != null)
        {
            // Evaluate again so edit-mode gizmos do not depend on a previous Update.
            Gizmos.color = HedefiKontrolEt(out _) ? Color.green : Color.red;
            Gizmos.DrawLine(transform.position, hedef.position);
            Vector3 boyut = hedef.lossyScale;
            float yaricap = .5f * Mathf.Max(Mathf.Abs(boyut.x), Mathf.Abs(boyut.y), Mathf.Abs(boyut.z));
            Gizmos.DrawWireSphere(hedef.position, Mathf.Max(.5f, yaricap));
        }

        Gizmos.color = oncekiRenk;
    }
}
