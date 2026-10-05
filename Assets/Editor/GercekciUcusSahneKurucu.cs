using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class GercekciUcusSahneKurucu
{
    public const string SahneYolu = "Assets/Generated/FlightGame/FlightGame.unity";
    private const string Folder = "Assets/Generated/FlightGame";
    private const string Staging = Folder + "/_Build/Rota2.unity";
    private const string Islem = "Gercekci Ucus Sahnesini Olustur";

    [MenuItem("Hangar 01/Gercekci Ucus Sahnesini Olustur")]
    public static void Olustur()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Once Play modundan cikin.");
            return;
        }
        // Never auto-save/discard a user's pending lesson changes while switching scenes.
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
            {
                Debug.LogWarning("Acik sahnelerde kaydedilmemis degisiklik var. Kendi degisikliklerinizi kaydedip menuyu tekrar calistirin; kaynak otomatik kaydedilmedi.");
                return;
            }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SahneYolu) != null)
        {
            EditorSceneManager.OpenScene(SahneYolu, OpenSceneMode.Single);
            Debug.Log("Mevcut FlightGame acildi; sahne yeniden olusturulmadi veya sifirlanmadi.");
            return;
        }
        var source = SceneManager.GetActiveScene();
        if (source.name != "Rota2" || string.IsNullOrEmpty(source.path))
            throw new InvalidOperationException("Ucagin bulundugu, kaydedilmis Rota2 sahnesini acin.");
        Root(source, "Nu_D36");
        Root(source, "Ucaksavar");
        Root(source, "Balon");
        string sourcePath = source.path;
        byte[] sourceBytes = File.ReadAllBytes(sourcePath);
        var sourceSetup = EditorSceneManager.GetSceneManagerSetup();
        EnsureFolder(Folder + "/_Build");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Staging) != null)
            throw new InvalidOperationException("Onceki kurulumun gecici sahnesi var: " + Staging + ". Incelemeden uzerine yazilmadi.");
        bool ownsStaging = false;
        bool completed = false;
        try
        {
            // Native scene copy preserves references between roots and all RenderSettings.
            // All following changes run ONLY in this copy, never in the lesson scene.
            if (!AssetDatabase.CopyAsset(sourcePath, Staging))
                throw new IOException("Rota2 kopyalanamadi.");
            ownsStaging = true;
            var game = EditorSceneManager.OpenScene(Staging, OpenSceneMode.Single);
            TerrainVerileriniAyir(game);
            // The staging file is named Rota2 so the existing safe, idempotent builder
            // can provide/repair terrain, sky and radar UI without modifying its code.
            ProfesyonelSahneKurucu.Kur();
            var plane = Root(game, "Nu_D36");
            var gun = Root(game, "Ucaksavar");
            var terrains = game.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Terrain>(true)).ToArray();
            if (terrains.Length == 0) throw new InvalidOperationException("Kullanilabilir Terrain yok.");
            var terrain = terrains.FirstOrDefault(t => t.name == "FlightTerrain");
            if (terrain == null) terrain = terrains[0];

            Vector3 startArea = plane.transform.position;
            float ground = terrain.SampleHeight(startArea) + terrain.transform.position.y;
            Vector3 runwayCenter = new Vector3(startArea.x, ground + .1f, startArea.z + 110f);
            PistiDuzlestir(terrain, runwayCenter, ground);
            GameObject runway = PistOlustur(runwayCenter);
            // Only the game copy's gun is moved if it obstructs the runway.
            if (Mathf.Abs(gun.transform.position.x - runwayCenter.x) < 16f
                && Mathf.Abs(gun.transform.position.z - runwayCenter.z) < 160f)
            {
                Vector3 p = gun.transform.position;
                var renderer = gun.GetComponent<Renderer>();
                float pivotHeight = renderer != null ? p.y - renderer.bounds.min.y : .5f;
                p.x = runwayCenter.x + 36f;
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y + pivotHeight;
                gun.transform.position = p;
            }

            Rigidbody rb = UcakFizigiKur(plane);
            var control = GetOrAdd<GercekciUcakKontrol>(plane);
            KontrolModu(plane);
            UcagiYereYerlestir(plane, rb, control, new Vector3(startArea.x, ground, startArea.z - 15f));
            Vector3 gunDirection = plane.transform.position - gun.transform.position;
            gunDirection.y = 0f;
            if (gunDirection.sqrMagnitude > .001f) gun.transform.rotation = Quaternion.LookRotation(gunDirection);
            OyunHudKur(game, control);
            KameraKur(game, plane.transform);
            Dogrula(game, plane, rb, control, runway);
            if (!sourceBytes.SequenceEqual(File.ReadAllBytes(sourcePath)))
                throw new InvalidOperationException("Kaynak Rota2 dosyasinin icerigi degisti; yeni sahne kaydedilmedi.");
            foreach (string guid in AssetDatabase.FindAssets("", new[] { Folder }))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && !(asset is SceneAsset)) AssetDatabase.SaveAssetIfDirty(asset);
            }
            // The final scene is saved only after construction and validation succeed.
            if (!EditorSceneManager.SaveScene(game, SahneYolu)) throw new IOException("FlightGame kaydedilemedi.");
            completed = true;
            Debug.Log("FlightGame hazir: " + SahneYolu + ". Baslangic gaz/hiz 0; W gaz, Asagi Ok kalkis, Space fren. Play testi yapilmadi.");
        }
        finally
        {
            if (!completed) EditorSceneManager.RestoreSceneManagerSetup(sourceSetup);
            if (ownsStaging) AssetDatabase.DeleteAsset(Staging);
        }
    }

    [MenuItem("Hangar 01/Gercekci Ucus Modunu Ac")]
    public static void GercekciModuAc()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != SahneYolu)
        {
            Debug.LogWarning("Bu komut yalnizca Assets/Generated/FlightGame/FlightGame.unity icin gecerlidir.");
            return;
        }
        var plane = Root(scene, "Nu_D36");
        if (plane.GetComponent<GercekciUcakKontrol>() == null)
        {
            Debug.LogWarning("Fizik kurulumu eksik. Ucak uzerinde GercekciUcakKontrol bulunamadi.");
            return;
        }
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Gercekci Ucus Modunu Ac");
        KontrolModu(plane);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static void KontrolModu(GameObject plane)
    {
        var physics = plane.GetComponent<GercekciUcakKontrol>();
        var manual = plane.GetComponent<UcakKontrol>();
        var auto = plane.GetComponent<HedefeGit>();
        if (physics != null) { Undo.RecordObject(physics, Islem); physics.enabled = true; }
        if (manual != null) { Undo.RecordObject(manual, Islem); manual.enabled = false; }
        if (auto != null) { Undo.RecordObject(auto, Islem); auto.enabled = false; }
    }

    private static void TerrainVerileriniAyir(Scene scene)
    {
        var copies = new Dictionary<TerrainData, TerrainData>();
        int index = 0;
        foreach (var terrain in scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Terrain>(true)))
        {
            if (terrain.terrainData == null) continue;
            TerrainData original = terrain.terrainData;
            if (!copies.TryGetValue(original, out var copy))
            {
                string path = Folder + "/TerrainCopy_" + index++ + ".asset";
                copy = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                if (copy == null)
                {
                    copy = Object.Instantiate(original);
                    copy.name = "FlightGameTerrain";
                    AssetDatabase.CreateAsset(copy, path);
                }
                copies.Add(original, copy);
            }
            terrain.terrainData = copy;
            GetOrAdd<TerrainCollider>(terrain.gameObject).terrainData = copy;
        }
        // A partially broken collider-only terrain must be isolated as well.
        foreach (var collider in scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<TerrainCollider>(true)))
        {
            var original = collider.terrainData;
            if (original == null || copies.Values.Contains(original)) continue;
            if (!copies.TryGetValue(original, out var copy))
            {
                copy = Object.Instantiate(original);
                AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(Folder + "/TerrainColliderCopy.asset"));
                copies.Add(original, copy);
            }
            collider.terrainData = copy;
        }
    }

    private static void PistiDuzlestir(Terrain terrain, Vector3 center, float height)
    {
        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        Vector3 size = data.size;
        if (center.x - 30 < origin.x || center.x + 30 > origin.x + size.x
            || center.z - 175 < origin.z || center.z + 175 > origin.z + size.z)
            throw new InvalidOperationException("Pist Terrain sinirlarina sigmiyor.");
        int n = data.heightmapResolution;
        var heights = data.GetHeights(0, 0, n, n);
        float flat = Mathf.Clamp01((height - origin.y) / size.y);
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            float wx = origin.x + x * size.x / (n - 1f);
            float wz = origin.z + z * size.z / (n - 1f);
            float dx = Mathf.Abs(wx - center.x), dz = Mathf.Abs(wz - center.z);
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Max(Mathf.InverseLerp(14f, 32f, dx), Mathf.InverseLerp(155f, 180f, dz)));
            heights[z, x] = Mathf.Lerp(flat, heights[z, x], blend);
        }
        data.SetHeights(0, 0, heights);
        terrain.Flush();
        GetOrAdd<TerrainCollider>(terrain.gameObject).terrainData = data;
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
    }

    private static GameObject PistOlustur(Vector3 center)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("URP Lit shader bulunamadi.");
        var asphalt = MaterialAsset("Asphalt", shader, new Color(.12f, .13f, .14f), .10f);
        var paint = MaterialAsset("RunwayPaint", shader, new Color(.84f, .84f, .77f), .12f);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/AsphaltTexture.asset");
        if (tex == null)
        {
            tex = new Texture2D(128, 128, TextureFormat.RGBA32, true) { name = "AsphaltGrain", wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color[128 * 128];
            var random = new System.Random(1421);
            for (int i = 0; i < pixels.Length; i++)
            {
                float value = .75f + (float)random.NextDouble() * .25f;
                pixels[i] = new Color(value, value, value, 1);
            }
            tex.SetPixels(pixels); tex.Apply();
            AssetDatabase.CreateAsset(tex, Folder + "/AsphaltTexture.asset");
        }
        asphalt.SetTexture("_BaseMap", tex);
        asphalt.SetTextureScale("_BaseMap", new Vector2(5, 60));
        EditorUtility.SetDirty(asphalt);
        var runway = new GameObject("Runway");
        runway.transform.position = center;
        var surface = Cube("Asphalt", runway.transform, Vector3.zero, new Vector3(18, .2f, 300), asphalt, true);
        surface.GetComponent<BoxCollider>().sharedMaterial = PhysicsMat("RunwayPhysics", .2f);
        for (int side = -1; side <= 1; side += 2)
            Cube("Edge_" + side, runway.transform, new Vector3(side * 8.25f, .106f, 0), new Vector3(.14f, .012f, 295), paint, false);
        for (int i = 0; i < 17; i++)
            Cube("CenterDash_" + i, runway.transform, new Vector3(0, .106f, -128 + i * 15), new Vector3(.35f, .012f, 7), paint, false);
        for (int i = 0; i < 8; i++)
            Cube("Threshold_" + i, runway.transform, new Vector3(-6.8f + i * 1.94f, .106f, -142), new Vector3(.65f, .012f, 9), paint, false);
        // A painted forward arrow marks the departure end/direction, without colliders.
        Cube("StartArrowStem", runway.transform, new Vector3(0, .107f, -115), new Vector3(.7f, .012f, 5), paint, false);
        var left = Cube("StartArrowLeft", runway.transform, new Vector3(-.8f, .107f, -112.8f), new Vector3(.5f, .012f, 2.6f), paint, false);
        left.transform.localRotation = Quaternion.Euler(0, 40, 0);
        var right = Cube("StartArrowRight", runway.transform, new Vector3(.8f, .107f, -112.8f), new Vector3(.5f, .012f, 2.6f), paint, false);
        right.transform.localRotation = Quaternion.Euler(0, -40, 0);
        return runway;
    }

    private static Rigidbody UcakFizigiKur(GameObject plane)
    {
        // These are scene components only. Imported visual meshes/materials are untouched.
        foreach (var collider in plane.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        var hull = new GameObject("FlightColliders");
        hull.transform.SetParent(plane.transform, false);
        var bodyMaterial = PhysicsMat("HullPhysics", .35f);
        var wheelMaterial = PhysicsMat("WheelPhysics", .015f);
        ColliderBox(hull.transform, "Fuselage", new Vector3(0, 1.63f, .0f), new Vector3(.8f, .8f, 5.9f), bodyMaterial);
        ColliderBox(hull.transform, "UpperWing", new Vector3(0, 2.9f, 1), new Vector3(9.4f, .14f, 1.4f), bodyMaterial);
        ColliderBox(hull.transform, "LowerWing", new Vector3(0, 1.15f, .7f), new Vector3(8.2f, .14f, 1.4f), bodyMaterial);
        ColliderBox(hull.transform, "LeftWheel", new Vector3(-.99f, .35f, 1.53f), new Vector3(.15f, .7f, .7f), wheelMaterial);
        ColliderBox(hull.transform, "RightWheel", new Vector3(.99f, .35f, 1.53f), new Vector3(.15f, .7f, .7f), wheelMaterial);
        ColliderBox(hull.transform, "TailSkid", new Vector3(0, .15f, -3.45f), new Vector3(.12f, .15f, .35f), wheelMaterial);
        ColliderBox(hull.transform, "Motor", new Vector3(0, 1.65f, 3), new Vector3(1, 1, .55f), bodyMaterial);
        var rb = GetOrAdd<Rigidbody>(plane);
        rb.mass = 650f;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearDamping = .005f;
        rb.angularDamping = .6f;
        rb.constraints = RigidbodyConstraints.None;
        rb.centerOfMass = new Vector3(0, 1.05f, .65f);
        rb.inertiaTensorRotation = Quaternion.identity;
        rb.inertiaTensor = new Vector3(1600, 2200, 3000);
        rb.maxAngularVelocity = 2f;
        rb.maxDepenetrationVelocity = 3f;
        rb.solverIterations = 10;
        rb.solverVelocityIterations = 6;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        return rb;
    }

    private static void UcagiYereYerlestir(GameObject plane, Rigidbody rb, GercekciUcakKontrol control, Vector3 spawn)
    {
        // Editor placement only. Runtime movement is exclusively forces/torques.
        rb.rotation = Quaternion.identity; // Runway +Z: no pitch or roll.
        rb.position = spawn + Vector3.up * 50f;
        Physics.SyncTransforms();
        if (!control.ZeminBul(spawn + Vector3.up * 100f, 300f, out var hit))
            throw new InvalidOperationException("Pist/zemin raycast ile bulunamadi.");
        var colliders = plane.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
        float bottom = colliders.Min(c => c.bounds.min.y);
        float pivotToWheel = rb.position.y - bottom;
        rb.position = new Vector3(spawn.x, hit.point.y + pivotToWheel + control.ZeminOffset, spawn.z);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
        float clearance = colliders.Min(c => c.bounds.min.y) - hit.point.y;
        if (clearance < -.01f || clearance > .15f)
            throw new InvalidOperationException("Tekerlek zemin yuksekligi dogrulanamadi: " + clearance);
    }

    private static void OyunHudKur(Scene scene, GercekciUcakKontrol control)
    {
        var canvas = Root(scene, "FlightHUD");
        var old = canvas.GetComponent<UcusHUD>();
        if (old != null) old.enabled = false;
        var metrics = canvas.transform.Find("FlightMetrics") as RectTransform;
        if (metrics == null) throw new InvalidOperationException("FlightMetrics UI bulunamadi.");
        metrics.sizeDelta = new Vector2(280, 194);
        var caption = metrics.Find("Caption").GetComponent<Text>();
        caption.text = "NU.D.36  /  FLIGHT GAME";
        caption.rectTransform.anchoredPosition = new Vector2(18, 75);
        var accent = metrics.Find("Accent") as RectTransform;
        if (accent != null) accent.sizeDelta = new Vector2(3, 166);
        Text speed = HudText(metrics, "Speed", "HIZ  0 km/h", 40, 23);
        Text altitude = HudText(metrics, "Altitude", $"İRTİFA  {control.transform.position.y:0} m", 5, 20);
        Text throttle = HudText(metrics, "Throttle", "GAZ  0 %", -30, 20);
        Text state = HudText(metrics, "State", "DURUM: YERDE", -65, 17);
        GetOrAdd<FlightHUD>(canvas).Kur(control, speed, altitude, throttle, state);
        // UcusRadarHUD is deliberately left enabled with the copied Radar references.
    }

    private static void KameraKur(Scene scene, Transform plane)
    {
        var camera = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Camera>(true))
            .Single(c => c.name == "Main Camera");
        camera.transform.SetParent(null, true);
        camera.fieldOfView = 65f;
        var follow = GetOrAdd<UcakTakipKamerasi>(camera.gameObject);
        follow.HedefAta(plane);
        follow.enabled = true;
        var settings = new SerializedObject(follow);
        settings.FindProperty("distance").floatValue = 13f;
        settings.FindProperty("height").floatValue = 4.5f;
        settings.FindProperty("lookAhead").floatValue = 2.5f;
        settings.FindProperty("pozisyonSmoothTime").floatValue = .3f;
        settings.FindProperty("donusYumusakligi").floatValue = 2.2f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        camera.transform.position = plane.position - Vector3.forward * 13f + Vector3.up * 4.5f;
        camera.transform.rotation = Quaternion.LookRotation(plane.position + Vector3.forward * 2.5f
            + Vector3.up * 1.2f - camera.transform.position, Vector3.up);
    }

    private static void Dogrula(Scene scene, GameObject plane, Rigidbody rb, GercekciUcakKontrol control, GameObject runway)
    {
        var auto = plane.GetComponent<HedefeGit>();
        var manual = plane.GetComponent<UcakKontrol>();
        if (!control.enabled || (auto != null && auto.enabled) || (manual != null && manual.enabled))
            throw new InvalidOperationException("Kontrol modlari cakisti.");
        if (!rb.useGravity || rb.isKinematic || rb.linearVelocity.sqrMagnitude > .001f)
            throw new InvalidOperationException("Rigidbody baslangic ayarlari yanlis.");
        if (runway.GetComponentsInChildren<Collider>().Count(c => c.enabled) != 1)
            throw new InvalidOperationException("Pist collider kurulumu yanlis.");
        var radar = Root(scene, "Ucaksavar").GetComponent<Radar>();
        if (radar == null || radar.hedef != plane.transform) throw new InvalidOperationException("Radar hedefi korunamadi.");
        if (Root(scene, "FlightHUD").GetComponent<UcusRadarHUD>() == null)
            throw new InvalidOperationException("Radar HUD bulunamadi.");
        var camera = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Camera>(true)).Single(c => c.name == "Main Camera");
        if (camera.transform.IsChildOf(plane.transform)) throw new InvalidOperationException("Kamera ucaga parent edilmis.");
    }

    private static Text HudText(RectTransform parent, string name, string value, float y, int size)
    {
        var child = parent.Find(name);
        if (child == null)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            child = obj.transform;
        }
        var text = GetOrAdd<Text>(child.gameObject);
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.text = value;
        text.color = new Color(.91f, .95f, .94f);
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, .5f);
        rect.anchoredPosition = new Vector2(18, y);
        rect.sizeDelta = new Vector2(250, 30);
        return text;
    }

    private static void ColliderBox(Transform parent, string name, Vector3 center, Vector3 size, PhysicsMaterial material)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = center;
        var collider = obj.AddComponent<BoxCollider>();
        collider.size = size;
        collider.sharedMaterial = material;
    }

    private static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 size, Material material, bool collision)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position;
        obj.transform.localScale = size;
        obj.GetComponent<MeshRenderer>().sharedMaterial = material;
        if (!collision) Object.DestroyImmediate(obj.GetComponent<BoxCollider>());
        return obj;
    }

    private static PhysicsMaterial PhysicsMat(string name, float friction)
    {
        string path = Folder + "/" + name + ".physicMaterial";
        var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (mat == null)
        {
            mat = new PhysicsMaterial(name);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.staticFriction = mat.dynamicFriction = friction;
        mat.bounciness = 0;
        mat.frictionCombine = PhysicsMaterialCombine.Minimum;
        mat.bounceCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material MaterialAsset(string name, Shader shader, Color color, float smoothness)
    {
        string path = Folder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader) { name = name }; AssetDatabase.CreateAsset(mat, path); }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static T GetOrAdd<T>(GameObject obj) where T : Component
    {
        var component = obj.GetComponent<T>();
        if (component == null) component = obj.AddComponent<T>();
        if (component == null) throw new InvalidOperationException(typeof(T).Name + " eklenemedi.");
        return component;
    }

    private static GameObject Root(Scene scene, string name)
    {
        var objects = scene.GetRootGameObjects().Where(o => o.name == name).ToArray();
        if (objects.Length != 1) throw new InvalidOperationException("Tek bir " + name + " gerekli.");
        return objects[0];
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
