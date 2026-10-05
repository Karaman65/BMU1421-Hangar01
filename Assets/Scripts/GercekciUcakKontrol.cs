using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
public class GercekciUcakKontrol : MonoBehaviour
{
    [Header("Gaz ve motor - oynanis baslangic ayarlari")]
    [SerializeField, Range(0f, 1f)] private float throttle;
    [SerializeField, Min(0f)] private float gazDegisimHizi = .45f;
    [SerializeField, Min(0f)] private float hizliGazDegisimi = .9f;
    [SerializeField, Min(0f)] private float motorItisi = 3600f;
    [Header("Aerodinamik - gercek ucak verisi degildir")]
    [SerializeField, Min(0f)] private float liftKatsayisi = 48f;
    [Tooltip("Yerel Z / ileri hava direnci.")]
    [SerializeField, Min(0f)] private float dragKatsayisi = 2.25f;
    [SerializeField, Min(0f)] private float yanDragKatsayisi = 18f;
    [SerializeField, Min(0f)] private float dikeyDragKatsayisi = 9f;
    [SerializeField, Min(0f)] private float yanKaymaSondurme = .65f;
    [SerializeField, Min(1f)] private float stallHizi = 9.5f;
    [SerializeField, Min(1f)] private float tamKontrolHizi = 15f;
    [SerializeField, Min(1f)] private float maksimumLiftG = 2.5f;
    [Header("Guvenli moment ust sinirlari (N m)")]
    [SerializeField, Min(0f)] private float pitchTorku = 6200f;
    [SerializeField, Min(0f)] private float rollTorku = 4800f;
    [SerializeField, Min(0f)] private float yawTorku = 1200f;
    [SerializeField, Min(0f)] private float yerYawTorku = 2000f;
    [SerializeField, Min(0f)] private float acisalSondurme = 650f;
    [Header("Hedef acisal hizlar (derece/saniye)")]
    [SerializeField, Min(0f)] private float maxPitchRate = 28f;
    [SerializeField, Min(0f)] private float maxRollRate = 65f;
    [Tooltip("Hiz hatasi (rad/s) basina moment. Pitch/roll moment ust sinirlari da uygulanir.")]
    [SerializeField, Min(0f)] private float pitchControlStrength = 9000f;
    [SerializeField, Min(0f)] private float rollControlStrength = 6500f;
    [SerializeField, Range(0f, 1f)] private float yawControlStrength = .65f;
    [SerializeField, Min(.1f)] private float pitchInputRiseSpeed = 2.5f;
    [SerializeField, Min(.1f)] private float pitchInputFallSpeed = 4f;
    [SerializeField, Min(.1f)] private float rollInputRiseSpeed = 3f;
    [SerializeField, Min(.1f)] private float rollInputFallSpeed = 4.5f;
    [Header("Yumusak pitch aci sinirlari")]
    [SerializeField, Range(25f, 40f)] private float maxNoseUpAngle = 33f;
    [SerializeField, Range(20f, 35f)] private float maxNoseDownAngle = 28f;
    [Header("Arcade-sim kontrol yardimlari")]
    [SerializeField, Min(.1f)] private float inputTepkisi = 6f;
    [SerializeField, Range(10f, 35f)] private float donusYatisAcisi = 28f;
    [SerializeField, Min(0f)] private float autoLevelStrength = 1.2f;
    [SerializeField, Min(0f)] private float stallBurunAsagiTorku = 400f;
    [SerializeField, HideInInspector] private int arcadeAyarSurumu;
    [Header("Zemin ve fren")]
    [SerializeField] private float zeminOffset = .015f;
    [SerializeField] private LayerMask zeminMaskesi = ~0;
    [SerializeField] private Vector3[] tekerRayNoktalari =
    {
        new Vector3(-.99f, .45f, 1.53f), new Vector3(.99f, .45f, 1.53f),
        new Vector3(0f, .30f, -3.45f)
    };
    [SerializeField, Min(.01f)] private float tekerRayMesafesi = .60f;
    [SerializeField, Min(0f)] private float frenIvmesi = 8f;

    private Rigidbody rb;
    private readonly RaycastHit[] temaslar = new RaycastHit[32];
    private float gazGirdisi, pitchGirdisi, turnInput, manualRollInput;
    private float smoothPitch, smoothRoll, smoothYaw, smoothTurn, smoothFren;
    private bool hizliGaz, fren;
    public float Gaz => throttle;
    public float Hiz => rb != null ? rb.linearVelocity.magnitude : 0f;
    public float IleriHiz { get; private set; }
    public bool Yerde { get; private set; }
    public bool Stall => !Yerde && IleriHiz < stallHizi;
    public float ZeminOffset => zeminOffset;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        EskiVarsayilanlariGuncelle();
        if (arcadeAyarSurumu < 2)
        {
            Guncelle(ref donusYatisAcisi, 40f, 28f);
            arcadeAyarSurumu = 2;
        }
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.maxAngularVelocity = 2f;
        // Initial conditions for this mode, not a velocity override every frame.
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        throttle = 0f;
    }

    private void Update()
    {
        var keys = Keyboard.current;
        if (keys == null || !Application.isFocused)
        {
            GirisiSifirla();
            return;
        }
        gazGirdisi = (keys.wKey.isPressed ? 1f : 0f) - (keys.sKey.isPressed ? 1f : 0f);
        pitchGirdisi = (keys.upArrowKey.isPressed ? 1f : 0f) - (keys.downArrowKey.isPressed ? 1f : 0f);
        turnInput = (keys.dKey.isPressed ? 1f : 0f) - (keys.aKey.isPressed ? 1f : 0f);
        // Q/E are MANUAL ROLL only: local +Z banks left, local -Z banks right.
        manualRollInput = (keys.qKey.isPressed ? 1f : 0f) - (keys.eKey.isPressed ? 1f : 0f);
        hizliGaz = keys.leftShiftKey.isPressed && !keys.sKey.isPressed;
        fren = keys.spaceKey.isPressed;
    }

    private void FixedUpdate()
    {
        if (rb == null) return;
        float dt = Time.fixedDeltaTime;
        throttle = Mathf.Clamp01(throttle + (hizliGaz ? hizliGazDegisimi : gazGirdisi * gazDegisimHizi) * dt);
        float inputStep = Mathf.Max(.1f, inputTepkisi) * dt;
        smoothPitch = InputYumusat(smoothPitch, pitchGirdisi, pitchInputRiseSpeed, pitchInputFallSpeed, dt);
        smoothRoll = InputYumusat(smoothRoll, manualRollInput, rollInputRiseSpeed, rollInputFallSpeed, dt);
        smoothTurn = Mathf.MoveTowards(smoothTurn, turnInput, inputStep);
        smoothYaw = Mathf.MoveTowards(smoothYaw, turnInput * .35f, inputStep);
        smoothFren = Mathf.MoveTowards(smoothFren, fren ? 1f : 0f, 3f * dt);
        Vector3 velocity = rb.linearVelocity;
        Vector3 localVelocity = transform.InverseTransformDirection(velocity);
        Vector3 localAngular = transform.InverseTransformDirection(rb.angularVelocity);
        IleriHiz = Vector3.Dot(velocity, transform.forward);
        float ileri = Mathf.Max(0f, IleriHiz);
        Yerde = YerTemasi();
        float stallFactor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(stallHizi * .45f, stallHizi, ileri));
        float speedControl = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, Mathf.Max(4f, tamKontrolHizi), ileri));
        // Keep some recovery authority during a stall instead of switching controls off.
        float airControlFactor = Mathf.Lerp(.25f, 1f, speedControl) * Mathf.Lerp(.65f, 1f, stallFactor);

        rb.AddForce(transform.forward * Mathf.Clamp(motorItisi, 0f, 100000f) * throttle, ForceMode.Force);
        // Lift builds continuously from 6 to 14 m/s, then increases with v squared.
        float liftFactor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 14f, ileri));
        float aoa = ileri > 1f ? Mathf.Atan2(-Vector3.Dot(velocity, transform.up), ileri) * Mathf.Rad2Deg : 0f;
        float aoaFactor = Mathf.Clamp(.65f + aoa * .055f, .25f, 1.3f);
        aoaFactor *= Mathf.Lerp(1f, .65f, Mathf.InverseLerp(22f, 45f, Mathf.Abs(aoa)));
        float lift = Mathf.Max(0f, liftKatsayisi) * ileri * ileri * liftFactor;
        lift *= aoaFactor * stallFactor;
        if (Yerde)
        {
            // Ground-running assist prevents hands-off lift-off even at high speed.
            // Pulling back releases the cap; speed and actual AoA still supply lift.
            float kalkisIstegi = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(-smoothPitch));
            lift = Mathf.Min(lift, rb.mass * Physics.gravity.magnitude * Mathf.Lerp(.72f, 1.65f, kalkisIstegi));
        }
        lift = Mathf.Clamp(lift, 0f, rb.mass * Physics.gravity.magnitude * Mathf.Clamp(maksimumLiftG, 1f, 4f));
        rb.AddForce(transform.up * lift, ForceMode.Force);
        YerelDirencUygula(localVelocity, speedControl, dt);

        float groundPitch = Mathf.Lerp(.02f * Mathf.Clamp01(ileri / 10f), 1f,
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 15f, ileri)));
        float pitchFactor = Yerde ? groundPitch : airControlFactor;
        float targetPitchRate = smoothPitch * Mathf.Max(0f, maxPitchRate) * Mathf.Deg2Rad;
        targetPitchRate *= PitchYonSiniri(targetPitchRate);
        // Rate error supplies both acceleration and braking. No direct angle/velocity writes.
        float pitchGain = Mathf.Max(0f, pitchControlStrength);
        if (Mathf.Abs(pitchGirdisi) < .01f && Mathf.Abs(smoothPitch) < .03f) pitchGain *= .65f;
        float pitchMoment = Mathf.Clamp((targetPitchRate - localAngular.x) * pitchGain,
            -pitchTorku, pitchTorku) * pitchFactor;
        float yawMoment = Yerde
            ? smoothTurn * yerYawTorku * Mathf.InverseLerp(.3f, 5f, velocity.magnitude)
            : smoothYaw * yawTorku * yawControlStrength * airControlFactor;

        Vector3 horizonUp = Vector3.ProjectOnPlane(Vector3.up, transform.forward);
        float bank = horizonUp.sqrMagnitude > .001f
            ? Vector3.SignedAngle(horizonUp, transform.up, transform.forward) : 0f;
        bool manualRollActive = Mathf.Abs(manualRollInput) > .01f || Mathf.Abs(smoothRoll) > .02f;
        float targetRollDegrees = 0f;
        if (manualRollActive)
        {
            // Manual rate has priority, even if A/D is held at the same time.
            // Keep priority during release smoothing so auto-bank cannot fight it.
            targetRollDegrees = smoothRoll * Mathf.Max(0f, maxRollRate);
        }
        else if (Mathf.Abs(turnInput) > .01f || Mathf.Abs(smoothTurn) > .03f)
        {
            float desiredBank = -smoothTurn * donusYatisAcisi;
            float errorDegrees = Mathf.DeltaAngle(bank, desiredBank);
            float coordinatedRateLimit = Mathf.Min(32f, Mathf.Max(0f, maxRollRate));
            targetRollDegrees = Mathf.Clamp(errorDegrees * 1.6f, -coordinatedRateLimit, coordinatedRateLimit);
        }
        else if (!Yerde)
        {
            // Gentle horizon recovery with a small dead zone, never an angle snap.
            float errorDegrees = Mathf.Abs(bank) > 2f ? Mathf.DeltaAngle(bank, 0f) : 0f;
            targetRollDegrees = Mathf.Clamp(errorDegrees * Mathf.Max(0f, autoLevelStrength) * .5f, -18f, 18f);
        }
        float rollRateLimit = Mathf.Max(0f, maxRollRate);
        float targetRollRate = Mathf.Clamp(targetRollDegrees, -rollRateLimit, rollRateLimit) * Mathf.Deg2Rad;
        float rollMoment = Mathf.Clamp((targetRollRate - localAngular.z) * Mathf.Max(0f, rollControlStrength),
            -rollTorku, rollTorku) * (Yerde ? airControlFactor * .015f : airControlFactor);
        if (!Yerde)
        {
            // No Q/E contribution to yaw; extra coordination is enabled only for A/D.
            if (!manualRollActive && Mathf.Abs(smoothTurn) > .03f)
                yawMoment -= Mathf.Sin(bank * Mathf.Deg2Rad) * yawTorku * .25f * yawControlStrength * airControlFactor;
            // Small, gradual nose-down recovery, reduced while the pilot is pitching.
            float recoveryDirection = Mathf.Sign(Vector3.Dot(transform.up, Vector3.up));
            pitchMoment += recoveryDirection * (1f - stallFactor) * stallBurunAsagiTorku
                * (1f - Mathf.Abs(smoothPitch)) * PitchYonSiniri(recoveryDirection);
        }
        // Pitch/roll rate controllers already damp their axes. Avoid double damping in air.
        Vector3 damping = new Vector3(Yerde ? localAngular.x * .35f : 0f,
            localAngular.y * 1.15f, Yerde ? localAngular.z * 1.5f : 0f);
        damping *= Mathf.Max(0f, acisalSondurme) * (Yerde ? 1.4f : 1f);
        Vector3 torque = transform.TransformDirection(new Vector3(pitchMoment, yawMoment, rollMoment) - damping);
        rb.AddTorque(Vector3.ClampMagnitude(torque, rb.mass * 25f), ForceMode.Force);

        if (Yerde)
        {
            // Tyre grip is separate from aerodynamic drag; only active on contact.
            Vector3 sideways = Vector3.Project(velocity, Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized);
            rb.AddForce(Vector3.ClampMagnitude(-sideways * rb.mass * 3f, rb.mass * 6f), ForceMode.Force);
            Vector3 horizontal = Vector3.ProjectOnPlane(velocity, Vector3.up);
            if (horizontal.sqrMagnitude > .0001f)
            {
                float rolling = Mathf.Min(.03f * horizontal.magnitude, .8f);
                float slowing = Mathf.Min(Mathf.Lerp(rolling, frenIvmesi, smoothFren), horizontal.magnitude / dt);
                rb.AddForce(-horizontal.normalized * rb.mass * Mathf.Max(0f, slowing), ForceMode.Force);
            }
        }
    }

    private static float InputYumusat(float current, float raw, float riseSpeed, float fallSpeed, float dt)
    {
        bool reducing = Mathf.Abs(raw) < Mathf.Abs(current) || current * raw < 0f;
        // On reversal, brake to zero first; the new direction then uses the rise speed.
        float target = current * raw < 0f ? 0f : raw;
        return Mathf.MoveTowards(current, target, Mathf.Max(.1f, reducing ? fallSpeed : riseSpeed) * dt);
    }

    private float PitchYonSiniri(float localPitchRate)
    {
        float elevation = Mathf.Asin(Mathf.Clamp(Vector3.Dot(transform.forward, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
        // Positive local X means nose down when upright; account for manual inverted roll.
        float verticalIntent = -localPitchRate * Vector3.Dot(transform.up, Vector3.up);
        if (verticalIntent > .0001f)
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(20f, maxNoseUpAngle, elevation));
        if (verticalIntent < -.0001f)
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, maxNoseDownAngle, -elevation));
        return 1f;
    }

    private void YerelDirencUygula(Vector3 velocity, float speedFactor, float dt)
    {
        Vector3 drag = new Vector3(
            -velocity.x * Mathf.Abs(velocity.x) * Mathf.Max(0f, yanDragKatsayisi),
            -velocity.y * Mathf.Abs(velocity.y) * Mathf.Max(0f, dikeyDragKatsayisi),
            -velocity.z * Mathf.Abs(velocity.z) * Mathf.Max(0f, dragKatsayisi));
        if (!Yerde) drag.x -= velocity.x * rb.mass * Mathf.Max(0f, yanKaymaSondurme) * speedFactor;
        // Each axis can dissipate energy, but cannot reverse that velocity in one step.
        for (int axis = 0; axis < 3; axis++)
        {
            float limit = rb.mass * Mathf.Min(12f, Mathf.Abs(velocity[axis]) / dt);
            drag[axis] = Mathf.Clamp(drag[axis], -limit, limit);
        }
        rb.AddForce(transform.TransformDirection(drag), ForceMode.Force);
    }

    private void EskiVarsayilanlariGuncelle()
    {
        // Existing FlightGame serializes the original settings. Upgrade only recognised
        // old defaults at runtime; retain deliberate Inspector customisations. No scene writes.
        if (arcadeAyarSurumu >= 1) return;
        Guncelle(ref gazDegisimHizi, .3f, .45f);
        Guncelle(ref hizliGazDegisimi, .7f, .9f);
        Guncelle(ref motorItisi, 2600f, 3600f);
        Guncelle(ref liftKatsayisi, 14f, 48f);
        Guncelle(ref dragKatsayisi, 2.2f, 2.25f);
        Guncelle(ref stallHizi, 16f, 9.5f);
        Guncelle(ref tamKontrolHizi, 25f, 15f);
        Guncelle(ref pitchTorku, 4500f, 6200f);
        Guncelle(ref rollTorku, 3000f, 4800f);
        Guncelle(ref yawTorku, 850f, 1200f);
        Guncelle(ref yerYawTorku, 2400f, 2000f);
        Guncelle(ref acisalSondurme, 900f, 650f);
        if (Mathf.Approximately(rb.angularDamping, .6f)) rb.angularDamping = .12f;
        arcadeAyarSurumu = 1;
    }

    private static void Guncelle(ref float value, float oldDefault, float newDefault)
    {
        if (Mathf.Approximately(value, oldDefault)) value = newDefault;
    }

    private bool YerTemasi()
    {
        if (tekerRayNoktalari == null) return false;
        foreach (Vector3 point in tekerRayNoktalari)
        {
            Vector3 origin = transform.TransformPoint(point);
            float length = point.z < -2f ? .38f : tekerRayMesafesi;
            if (ZeminBul(origin, Mathf.Max(.01f, length), out _)) return true;
        }
        return false;
    }

    // Used by the Editor builder too. Ignore self, triggers and other additive scenes.
    public bool ZeminBul(Vector3 origin, float mesafe, out RaycastHit enYakin)
    {
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, temaslar, mesafe,
            zeminMaskesi, QueryTriggerInteraction.Ignore);
        enYakin = default;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = temaslar[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform)
                || hit.collider.gameObject.scene != gameObject.scene || hit.normal.y < .45f) continue;
            if (hit.distance < nearest) { nearest = hit.distance; enYakin = hit; }
        }
        return nearest < float.PositiveInfinity;
    }

    private void GirisiSifirla()
    {
        gazGirdisi = pitchGirdisi = turnInput = manualRollInput = 0f;
        hizliGaz = fren = false;
    }
    private void OnDisable()
    {
        GirisiSifirla();
        smoothPitch = smoothRoll = smoothYaw = smoothTurn = smoothFren = 0f;
    }
}
