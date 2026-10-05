using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UcusHUD : MonoBehaviour
{
    [SerializeField] private Transform ucak;
    [SerializeField] private Text hizYazisi;
    [SerializeField] private Text irtifaYazisi;
    private Vector3 oncekiKonum;
    private bool ilkOrnek = true;
    private float sonrakiGuncelleme;
    private float toplamMesafe;
    private float toplamSure;

    public void Kur(Transform hedef, Text hiz, Text irtifa)
    {
        ucak = hedef;
        hizYazisi = hiz;
        irtifaYazisi = irtifa;
        OlcumuSifirla();
    }

    private void OnEnable() { OlcumuSifirla(); }

    private void OlcumuSifirla()
    {
        ilkOrnek = true;
        toplamMesafe = toplamSure = sonrakiGuncelleme = 0f;
    }

    private void LateUpdate()
    {
        if (ucak == null) return;
        if (ilkOrnek)
        {
            oncekiKonum = ucak.position;
            ilkOrnek = false;
        }
        if (Time.deltaTime > 0f)
        {
            toplamMesafe += Vector3.Distance(oncekiKonum, ucak.position);
            toplamSure += Time.deltaTime;
        }
        oncekiKonum = ucak.position;
        // Ten text refreshes per second keep the readout calm and avoid per-frame strings.
        if (Time.unscaledTime < sonrakiGuncelleme) return;
        sonrakiGuncelleme = Time.unscaledTime + .1f;
        float hiz = toplamSure > 0f ? toplamMesafe / toplamSure : 0f;
        if (hizYazisi != null) hizYazisi.text = $"HIZ  {hiz:0} m/s";
        if (irtifaYazisi != null) irtifaYazisi.text = $"İRTİFA  {ucak.position.y:0} m";
        toplamMesafe = toplamSure = 0f;
    }
}
