using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UcusRadarHUD : MonoBehaviour
{
    [SerializeField] private Transform oyuncu;
    [SerializeField] private Transform ucaksavar;
    [SerializeField] private Transform balon;
    [SerializeField] private Radar radar;
    [SerializeField] private RectTransform ucaksavarIsareti;
    [SerializeField] private RectTransform balonIsareti;
    [SerializeField] private Image ucaksavarGorseli;
    [SerializeField] private Image balonGorseli;
    [SerializeField] private Outline cerceve;
    [SerializeField] private CanvasGroup uyari;
    [SerializeField] private Text mesafeYazisi;
    [SerializeField, Min(1f)] private float menzil = 250f;
    [SerializeField, Min(1f)] private float panelYaricapi = 96f;
    private Vector3 sonYatayYon = Vector3.forward;
    private float sonrakiYazi;
    private static readonly Color Tehlike = new Color(1f, .32f, .22f);
    private static readonly Color Hedef = new Color(.42f, .9f, .82f);
    private static readonly Color NormalCerceve = new Color(.55f, .72f, .76f, .5f);

    public void Kur(Transform pilot, Transform silah, Transform hedef, Radar kaynak,
        Image silahBlip, Image hedefBlip, Outline kenar, CanvasGroup mesaj, Text mesafe)
    {
        oyuncu = pilot;
        ucaksavar = silah;
        balon = hedef;
        radar = kaynak;
        ucaksavarGorseli = silahBlip;
        balonGorseli = hedefBlip;
        ucaksavarIsareti = silahBlip.rectTransform;
        balonIsareti = hedefBlip.rectTransform;
        cerceve = kenar;
        uyari = mesaj;
        mesafeYazisi = mesafe;
        if (uyari != null) uyari.alpha = 0f;
    }

    private void LateUpdate()
    {
        // The existing lesson radar is the sole source of detection truth.
        bool tespit = radar != null && radar.Goruyor;
        float nabiz = .5f + .5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * .8f);
        if (uyari != null)
            uyari.alpha = tespit ? Mathf.Lerp(uyari.alpha, .75f + .25f * nabiz,
                1f - Mathf.Exp(-6f * Time.unscaledDeltaTime)) : 0f;
        if (cerceve != null)
            cerceve.effectColor = tespit ? new Color(1f, .3f, .2f, .45f + .4f * nabiz) : NormalCerceve;
        if (oyuncu == null) return;

        Vector3 ileri = Vector3.ProjectOnPlane(oyuncu.forward, Vector3.up);
        if (ileri.sqrMagnitude > .001f) sonYatayYon = ileri.normalized;
        Vector3 sag = Vector3.Cross(Vector3.up, sonYatayYon);
        IsaretGuncelle(ucaksavar, ucaksavarIsareti, ucaksavarGorseli, sag, Tehlike);
        IsaretGuncelle(balon, balonIsareti, balonGorseli, sag, Hedef);
        if (mesafeYazisi != null && Time.unscaledTime >= sonrakiYazi)
        {
            mesafeYazisi.text = ucaksavar != null
                ? $"UÇAKSAVAR: {Vector3.Distance(oyuncu.position, ucaksavar.position):0} m"
                : "UÇAKSAVAR: —";
            sonrakiYazi = Time.unscaledTime + .1f;
        }
    }

    private void IsaretGuncelle(Transform hedef, RectTransform isaret, Image gorsel, Vector3 sag, Color renk)
    {
        if (isaret == null || gorsel == null) return;
        gorsel.enabled = hedef != null;
        if (hedef == null) return;
        Vector3 fark = hedef.position - oyuncu.position;
        fark.y = 0f;
        Vector2 duzlem = new Vector2(Vector3.Dot(fark, sag), Vector3.Dot(fark, sonYatayYon));
        float etkinMenzil = Mathf.Max(1f, menzil);
        isaret.anchoredPosition = Vector2.ClampMagnitude(duzlem / etkinMenzil, 1f) * panelYaricapi;
        renk.a = duzlem.sqrMagnitude > etkinMenzil * etkinMenzil ? .4f : 1f;
        gorsel.color = renk;
    }

    private void OnDisable()
    {
        if (uyari != null) uyari.alpha = 0f;
        if (cerceve != null) cerceve.effectColor = NormalCerceve;
    }
}
