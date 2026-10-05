using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class FlightHUD : MonoBehaviour
{
    [SerializeField] private GercekciUcakKontrol ucak;
    [SerializeField] private Text hiz;
    [SerializeField] private Text irtifa;
    [SerializeField] private Text gaz;
    [SerializeField] private Text durum;
    private float sonrakiGuncelleme;

    public void Kur(GercekciUcakKontrol hedef, Text hizYazisi, Text irtifaYazisi, Text gazYazisi, Text durumYazisi)
    {
        ucak = hedef;
        hiz = hizYazisi;
        irtifa = irtifaYazisi;
        gaz = gazYazisi;
        durum = durumYazisi;
    }

    private void LateUpdate()
    {
        if (ucak == null || Time.unscaledTime < sonrakiGuncelleme) return;
        sonrakiGuncelleme = Time.unscaledTime + .1f;
        if (hiz != null) hiz.text = $"HIZ  {ucak.Hiz * 3.6f:0} km/h";
        if (irtifa != null) irtifa.text = $"İRTİFA  {ucak.transform.position.y:0} m";
        if (gaz != null) gaz.text = $"GAZ  {ucak.Gaz * 100f:0} %";
        if (durum != null)
        {
            durum.text = "DURUM: " + (ucak.Yerde ? "YERDE" : ucak.Stall ? "STALL" : "HAVADA");
            durum.color = ucak.Stall ? new Color(1f, .53f, .28f) : new Color(.7f, .9f, .84f);
        }
    }
}
