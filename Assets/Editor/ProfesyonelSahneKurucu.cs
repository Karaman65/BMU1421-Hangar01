using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Uses the project's installed URP 17.3 and uGUI APIs. No runtime editor dependency.
public static class ProfesyonelSahneKurucu
{
    private const string Islem = "Profesyonel Oyun Sahnesini Kur";
    private static readonly Color Ink = new Color(.025f, .05f, .065f, .84f);
    private static readonly Color White = new Color(.91f, .95f, .94f);
    private static readonly Color Muted = new Color(.55f, .7f, .73f);

    [MenuItem("Hangar 01/Profesyonel Oyun Sahnesini Kur")]
    public static void Kur()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Kurulum icin Play modundan cikin.");
            return;
        }
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.name != "Rota2")
            throw new InvalidOperationException("Nesneleri bulunan Rota2 sahnesini aktif hale getirin.");
        var roots = scene.GetRootGameObjects();
        var plane = RequireRoot(roots, "Nu_D36");
        var gun = RequireRoot(roots, "Ucaksavar");
        var balloon = RequireRoot(roots, "Balon");
        var cameras = roots.SelectMany(o => o.GetComponentsInChildren<Camera>(true))
            .Where(c => c.name == "Main Camera").ToArray();
        if (cameras.Length != 1) throw new InvalidOperationException("Tek bir Main Camera gerekli.");
        var camera = cameras[0];
        var radar = gun.GetComponent<Radar>();
        if (radar == null) throw new InvalidOperationException("Ucaksavar uzerinde mevcut Radar gerekli.");
        if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
            throw new InvalidOperationException("Bu kurucu mevcut URP projesi icindir. Aktif URP bulunamadi.");
        var terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        var skyShader = Shader.Find("Skybox/Procedural");
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (terrainShader == null || skyShader == null || font == null)
            throw new InvalidOperationException("Terrain/skybox shader veya UI fontu bulunamadi.");

        // Stable per-scene asset folder. Repeated runs reuse assets and named objects.
        string sceneId = AssetDatabase.AssetPathToGUID(scene.path);
        if (string.IsNullOrEmpty(sceneId))
            throw new InvalidOperationException("Once Rota2 sahnesine bir dosya yolu verin.");
        string folder = "Assets/Generated/ProfesyonelRota2/" + sceneId;
        EnsureFolder(folder);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(Islem);
        Vector3 planePosition = plane.transform.position;
        Vector3 gunPosition = gun.transform.position;
        Vector3 balloonPosition = balloon.transform.position;
        try
        {
            var environment = NamedObject(scene, null, "Environment");
            TerrainKur(scene, environment.transform, plane.transform, gun, folder, terrainShader);
            AtmosferKur(scene, camera, folder, skyShader);
            KameraKur(camera, plane.transform);
            HudKur(scene, plane.transform, gun.transform, balloon.transform, radar, font, folder);

            // Only generated assets are saved. The user's scene stays dirty and unsaved.
            foreach (string guid in AssetDatabase.FindAssets("", new[] { folder }))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
            }
            if (plane.transform.position != planePosition || gun.transform.position != gunPosition
                || balloon.transform.position != balloonPosition)
                throw new InvalidOperationException("Korunan nesne konumlari degisti.");
            if (scene.GetRootGameObjects().Count(o => o.name == "Environment") != 1
                || scene.GetRootGameObjects().Count(o => o.name == "FlightHUD") != 1)
                throw new InvalidOperationException("Environment/HUD tekillik kontrolu basarisiz.");
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            SceneView.RepaintAll();
            Debug.Log("Profesyonel ortam ve HUD hazir. Sahne kaydedilmedi; Ctrl+S ile kaydedebilirsiniz.");
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }

    private static void TerrainKur(Scene scene, Transform parent, Transform plane, GameObject gun,
        string folder, Shader shader)
    {
        // Also find a partially created terrain left at the scene root.
        var matches = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(t => t.name == "FlightTerrain").ToArray();
        if (matches.Length > 1)
            throw new InvalidOperationException("Birden fazla FlightTerrain bulundu; otomatik kopya olusturulmadi.");
        GameObject obj = matches.Length == 1 ? matches[0].gameObject : null;

        // Reuse attached/canonical/legacy data; do not duplicate assets from an earlier run.
        TerrainData data = null;
        if (obj != null)
        {
            var existingTerrain = obj.GetComponent<Terrain>();
            var existingCollider = obj.GetComponent<TerrainCollider>();
            if (existingTerrain != null) data = existingTerrain.terrainData;
            if (data == null && existingCollider != null) data = existingCollider.terrainData;
        }
        string sceneId = AssetDatabase.AssetPathToGUID(scene.path);
        string path = "Assets/Generated/Environment/FlightTerrain_" + sceneId + ".asset";
        if (data == null) data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        if (data == null) data = AssetDatabase.LoadAssetAtPath<TerrainData>(folder + "/FlightTerrain.asset");
        if (data == null)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException("Terrain asset yolunda farkli bir asset var: " + path);
            data = new TerrainData
            {
                name = "FlightTerrain",
                heightmapResolution = 257,
                size = new Vector3(1200f, 100f, 1200f)
            };
        }
        // Persist transient data too; the scene and collider retain the same object.
        if (!AssetDatabase.Contains(data))
        {
            EnsureFolder("Assets/Generated/Environment");
            var savedData = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (savedData != null)
                data = savedData;
            else
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                    throw new InvalidOperationException("Terrain asset yolu kullaniliyor: " + path);
                AssetDatabase.CreateAsset(data, path);
            }
        }

        if (obj == null)
        {
            obj = Terrain.CreateTerrainGameObject(data);
            if (obj == null) throw new InvalidOperationException("Terrain nesnesi olusturulamadi.");
            obj.name = "FlightTerrain";
            SceneManager.MoveGameObjectToScene(obj, scene);
            Undo.RegisterCreatedObjectUndo(obj, Islem);
        }
        if (obj.transform.parent != parent)
            Undo.SetTransformParent(obj.transform, parent, Islem);

        // Use Unity's overloaded null comparison, never ??, for missing components.
        var terrain = obj.GetComponent<Terrain>();
        if (terrain == null)
        {
            Undo.AddComponent<Terrain>(obj);
            terrain = obj.GetComponent<Terrain>();
        }
        var collider = obj.GetComponent<TerrainCollider>();
        if (collider == null)
        {
            Undo.AddComponent<TerrainCollider>(obj);
            collider = obj.GetComponent<TerrainCollider>();
        }
        if (terrain == null || collider == null)
            throw new InvalidOperationException("FlightTerrain: Terrain/TerrainCollider eklenemedi.");
        Undo.RecordObjects(new Object[] { terrain, collider }, Islem);
        terrain.terrainData = data;
        collider.terrainData = data;
        if (terrain.terrainData != data || collider.terrainData != data)
            throw new InvalidOperationException("FlightTerrain: ortak TerrainData atanamadi.");
        Undo.RegisterCompleteObjectUndo(data, Islem);
        data.heightmapResolution = 257;
        data.alphamapResolution = 128;
        data.baseMapResolution = 512;
        data.size = new Vector3(1200f, 100f, 1200f);

        // Put the flat pad at the gun base, without moving gun, aircraft or balloon.
        var gunRenderer = gun.GetComponent<Renderer>();
        float ground = gunRenderer != null ? gunRenderer.bounds.min.y : gun.transform.position.y - .5f;
        Vector3 origin = new Vector3(plane.position.x - 600f, ground, plane.position.z - 600f);
        var heights = new float[257, 257];
        for (int z = 0; z < 257; z++)
        for (int x = 0; x < 257; x++)
        {
            float wx = origin.x + x * 1200f / 256f;
            float wz = origin.z + z * 1200f / 256f;
            float broad = Mathf.PerlinNoise(wx / 360f + 23.4f, wz / 360f + 12.7f);
            float detail = Mathf.PerlinNoise(wx / 135f + 81f, wz / 135f + 31f);
            float altitude = 6f + broad * 29f + detail * 8f;
            float startDistance = Vector2.Distance(new Vector2(wx, wz), new Vector2(plane.position.x, plane.position.z));
            float gunDistance = Vector2.Distance(new Vector2(wx, wz), new Vector2(gun.transform.position.x, gun.transform.position.z));
            float startBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(120f, 290f, startDistance));
            float gunBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(24f, 100f, gunDistance));
            heights[z, x] = altitude * startBlend * gunBlend / 100f;
        }
        data.SetHeights(0, 0, heights);
        var grass = Layer(folder, "DryGrass", new Color(.40f, .44f, .29f), new Color(.58f, .55f, .37f), 17f);
        var soil = Layer(folder, "Earth", new Color(.38f, .32f, .25f), new Color(.58f, .49f, .36f), 21f);
        var stone = Layer(folder, "Stone", new Color(.43f, .44f, .40f), new Color(.61f, .60f, .52f), 29f);
        data.terrainLayers = new[] { grass, soil, stone };
        var weights = new float[128, 128, 3];
        for (int z = 0; z < 128; z++)
        for (int x = 0; x < 128; x++)
        {
            float u = x / 127f, v = z / 127f;
            float slope = Mathf.Clamp01(data.GetSteepness(u, v) / 32f);
            float patches = Mathf.PerlinNoise(u * 8f + 17f, v * 8f + 9f);
            weights[z, x, 2] = slope * .7f;
            weights[z, x, 1] = (1f - weights[z, x, 2]) * Mathf.Lerp(.1f, .65f, patches);
            weights[z, x, 0] = 1f - weights[z, x, 1] - weights[z, x, 2];
        }
        data.SetAlphamaps(0, 0, weights);
        EditorUtility.SetDirty(data);
        Undo.RecordObjects(new Object[] { terrain, collider, obj.transform }, Islem);
        obj.transform.SetPositionAndRotation(origin, Quaternion.identity);
        obj.transform.localScale = Vector3.one;
        terrain.terrainData = data;
        collider.terrainData = data;
        terrain.materialTemplate = MaterialAsset(folder + "/Terrain.mat", shader);
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 8f;
        terrain.basemapDistance = 400f;
        terrain.detailObjectDistance = 0f;
        terrain.treeDistance = 0f;
        terrain.Flush();
        float groundAtGun = terrain.SampleHeight(gun.transform.position) + terrain.transform.position.y;
        if (groundAtGun > ground + .05f)
            throw new InvalidOperationException("Ucaksavar duz zemin kontrolu basarisiz.");
        // New data lives outside the material folder, so save this generated asset explicitly.
        AssetDatabase.SaveAssetIfDirty(data);
    }

    private static TerrainLayer Layer(string folder, string name, Color a, Color b, float tile)
    {
        string texturePath = folder + "/" + name + "Texture.asset";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
        {
            texture = new Texture2D(128, 128, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color[128 * 128];
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
            {
                // Periodic noise keeps tiles seamless at their borders.
                float u = x / 128f, v = y / 128f;
                float n0 = Mathf.PerlinNoise(u * 5f, v * 5f);
                float n1 = Mathf.PerlinNoise((u - 1) * 5f, v * 5f);
                float n2 = Mathf.PerlinNoise(u * 5f, (v - 1) * 5f);
                float n3 = Mathf.PerlinNoise((u - 1) * 5f, (v - 1) * 5f);
                float n = Mathf.Lerp(Mathf.Lerp(n0, n1, u), Mathf.Lerp(n2, n3, u), v);
                pixels[y * 128 + x] = Color.Lerp(a, b, n);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            AssetDatabase.CreateAsset(texture, texturePath);
        }
        string path = folder + "/" + name + ".terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = new TerrainLayer { name = name };
            AssetDatabase.CreateAsset(layer, path);
        }
        Undo.RecordObject(layer, Islem);
        layer.diffuseTexture = texture;
        layer.tileSize = new Vector2(tile, tile);
        layer.metallic = 0f;
        layer.smoothness = .05f;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    private static void AtmosferKur(Scene scene, Camera camera, string folder, Shader skyShader)
    {
        var lights = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Light>(true)).ToArray();
        var sun = lights.FirstOrDefault(l => l.name == "Directional Light" && l.type == LightType.Directional)
            ?? lights.FirstOrDefault(l => l.type == LightType.Directional);
        if (sun == null) sun = Component<Light>(NamedObject(scene, null, "Directional Light"));
        Undo.RecordObjects(new Object[] { sun, sun.transform }, Islem);
        sun.enabled = true;
        sun.type = LightType.Directional;
        sun.color = new Color(1f, .94f, .84f);
        sun.intensity = 1.65f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = .8f;
        sun.shadowBias = .04f;
        sun.transform.rotation = Quaternion.Euler(32f, -38f, 0f);

        Undo.RegisterCompleteObjectUndo(Unsupported.GetRenderSettings(), Islem);
        var sky = MaterialAsset(folder + "/DaySky.mat", skyShader);
        Undo.RecordObject(sky, Islem);
        sky.SetFloat("_SunDisk", 2f);
        sky.EnableKeyword("_SUNDISK_HIGH_QUALITY");
        sky.SetFloat("_SunSize", .035f);
        sky.SetFloat("_AtmosphereThickness", .9f);
        sky.SetFloat("_Exposure", 1.15f);
        sky.SetColor("_SkyTint", new Color(.51f, .61f, .72f));
        sky.SetColor("_GroundColor", new Color(.42f, .44f, .38f));
        EditorUtility.SetDirty(sky);
        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.57f, .68f, .8f);
        RenderSettings.ambientEquatorColor = new Color(.44f, .49f, .51f);
        RenderSettings.ambientGroundColor = new Color(.27f, .26f, .22f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(.65f, .75f, .82f);
        RenderSettings.fogDensity = .00125f;

        var volumes = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Volume>(true)).ToArray();
        var volume = volumes.FirstOrDefault(v => v.name == "Global Volume")
            ?? volumes.FirstOrDefault(v => v.isGlobal);
        if (volume == null) volume = Component<Volume>(NamedObject(scene, null, "Global Volume"));
        string profilePath = folder + "/FlightVolume.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
        if (profile == null)
        {
            // Copy all existing overrides once; never edit a shared original profile.
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
            if (volume.sharedProfile != null)
                foreach (var original in volume.sharedProfile.components)
                {
                    var copy = Object.Instantiate(original);
                    profile.components.Add(copy);
                    AssetDatabase.AddObjectToAsset(copy, profile);
                }
        }
        Undo.RecordObject(volume, Islem);
        volume.sharedProfile = profile;
        volume.isGlobal = true;
        volume.enabled = true;
        volume.weight = 1f;
        var bloom = Override<Bloom>(profile);
        bloom.intensity.Override(.18f);
        bloom.threshold.Override(1.15f);
        bloom.scatter.Override(.55f);
        var color = Override<ColorAdjustments>(profile);
        color.postExposure.Override(.1f);
        color.contrast.Override(7f);
        color.saturation.Override(-5f);
        var vignette = Override<Vignette>(profile);
        vignette.intensity.Override(.13f);
        vignette.smoothness.Override(.45f);
        var tone = Override<Tonemapping>(profile);
        tone.mode.Override(TonemappingMode.ACES);
        foreach (var item in profile.components) EditorUtility.SetDirty(item);
        EditorUtility.SetDirty(profile);
        var additional = Component<UniversalAdditionalCameraData>(camera.gameObject);
        Undo.RecordObject(additional, Islem);
        additional.renderPostProcessing = true;
        additional.volumeLayerMask |= 1 << volume.gameObject.layer;
    }

    private static T Override<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet<T>(out var item))
        {
            Undo.RecordObject(profile, Islem);
            item = profile.Add<T>(false);
            AssetDatabase.AddObjectToAsset(item, profile);
        }
        Undo.RecordObject(item, Islem);
        item.active = true;
        return item;
    }

    private static void KameraKur(Camera camera, Transform plane)
    {
        Undo.RecordObject(camera, Islem);
        camera.fieldOfView = 65f;
        camera.farClipPlane = Mathf.Max(camera.farClipPlane, 1800f);
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.allowHDR = true;
        if (camera.transform.IsChildOf(plane)) Undo.SetTransformParent(camera.transform, null, Islem);
        var follow = Component<UcakTakipKamerasi>(camera.gameObject);
        Undo.RecordObject(follow, Islem);
        follow.HedefAta(plane);
        follow.enabled = true;
    }

    private static void HudKur(Scene scene, Transform plane, Transform gun, Transform balloon,
        Radar radar, Font font, string folder)
    {
        var root = NamedObject(scene, null, "FlightHUD", true);
        var canvas = Component<Canvas>(root);
        Undo.RecordObject(canvas, Islem);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = Component<CanvasScaler>(root);
        Undo.RecordObject(scaler, Islem);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = .5f;
        // Read-only UI: no EventSystem or input module is added.

        var metrics = Panel(scene, root.transform, "FlightMetrics", new Vector2(0, 0),
            new Vector2(0, 0), new Vector2(36, 36), new Vector2(246, 118), Ink);
        Panel(scene, metrics.transform, "Accent", new Vector2(0, .5f), new Vector2(0, .5f),
            Vector2.zero, new Vector2(3, 92), new Color(.4f, .82f, .78f));
        Label(scene, metrics.transform, "Caption", font, "NU.D.36  /  UÇUŞ VERİSİ", 12,
            new Vector2(18, 39), new Vector2(218, 20), Muted, TextAnchor.MiddleLeft);
        var speed = Label(scene, metrics.transform, "Speed", font, "HIZ  0 m/s", 25,
            new Vector2(18, 5), new Vector2(218, 36), White, TextAnchor.MiddleLeft);
        var altitude = Label(scene, metrics.transform, "Altitude", font, $"İRTİFA  {plane.position.y:0} m", 20,
            new Vector2(18, -31), new Vector2(218, 30), White, TextAnchor.MiddleLeft);
        var hud = Component<UcusHUD>(root);
        Undo.RecordObject(hud, Islem);
        hud.Kur(plane, speed, altitude);

        var panel = Panel(scene, root.transform, "RadarPanel", new Vector2(1, 0),
            new Vector2(1, 0), new Vector2(-36, 72), new Vector2(230, 230), Ink);
        var border = Component<Outline>(panel.gameObject);
        Undo.RecordObject(border, Islem);
        border.effectColor = new Color(.55f, .72f, .76f, .5f);
        border.effectDistance = new Vector2(1, -1);
        var grid = Panel(scene, panel.transform, "RangeRings", new Vector2(.5f, .5f),
            new Vector2(.5f, .5f), Vector2.zero, new Vector2(230, 230), Color.white);
        grid.sprite = RadarSprite(folder);
        Label(scene, panel.transform, "Heading", font, "İLERİ", 10,
            new Vector2(0, 96), new Vector2(100, 18), Muted);
        Label(scene, panel.transform, "Title", font, "RADAR   /   250 m", 13,
            new Vector2(0, 135), new Vector2(230, 24), White);
        var distance = Label(scene, panel.transform, "Distance", font, "UÇAKSAVAR: —", 15,
            new Vector2(0, -137), new Vector2(260, 24), White);
        Label(scene, panel.transform, "Legend", font, "◆ TEHDİT     ■ BALON", 10,
            new Vector2(0, -160), new Vector2(250, 20), Muted);
        var danger = Panel(scene, panel.transform, "GunBlip", new Vector2(.5f, .5f),
            new Vector2(.5f, .5f), Vector2.zero, new Vector2(11, 11), new Color(1f, .32f, .22f));
        Undo.RecordObject(danger.transform, Islem);
        danger.transform.localRotation = Quaternion.Euler(0, 0, 45);
        var target = Panel(scene, panel.transform, "BalloonBlip", new Vector2(.5f, .5f),
            new Vector2(.5f, .5f), new Vector2(0, 28), new Vector2(9, 9), new Color(.42f, .9f, .82f));

        var warning = Panel(scene, root.transform, "RadarWarning", new Vector2(.5f, 1),
            new Vector2(.5f, 1), new Vector2(0, -36), new Vector2(410, 76), new Color(.24f, .055f, .035f, .86f));
        Panel(scene, warning.transform, "WarningLine", new Vector2(.5f, 1), new Vector2(.5f, 1),
            Vector2.zero, new Vector2(410, 2), new Color(1f, .36f, .2f));
        Label(scene, warning.transform, "Title", font, "RADAR TESPİTİ", 23,
            new Vector2(0, 12), new Vector2(390, 30), new Color(1f, .77f, .62f));
        Label(scene, warning.transform, "Subtitle", font, "UÇAKSAVAR SENİ GÖRÜYOR", 12,
            new Vector2(0, -18), new Vector2(390, 24), White);
        var group = Component<CanvasGroup>(warning.gameObject);
        Undo.RecordObject(group, Islem);
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        var radarHud = Component<UcusRadarHUD>(root);
        Undo.RecordObject(radarHud, Islem);
        radarHud.Kur(plane, gun, balloon, radar, danger, target, border, group, distance);
    }

    private static Sprite RadarSprite(string folder)
    {
        string path = folder + "/RadarGrid.asset";
        var existing = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        if (existing != null) return existing;
        var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false)
            { name = "RadarGrid", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color[256 * 256];
        float[] radii = { 35f, 71f, 107f };
        for (int y = 0; y < 256; y++)
        for (int x = 0; x < 256; x++)
        {
            float dx = x - 127.5f, dy = y - 127.5f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float alpha = 0f;
            foreach (float radius in radii)
                alpha = Mathf.Max(alpha, Mathf.Clamp01(1.4f - Mathf.Abs(r - radius)) * .34f);
            if ((Mathf.Abs(dx) < .8f || Mathf.Abs(dy) < .8f) && r < 107f) alpha = Mathf.Max(alpha, .14f);
            bool arrow = dy >= -5f && dy <= 9f && Mathf.Abs(dx) <= (9f - dy) * .45f;
            pixels[y * 256 + x] = arrow ? new Color(.83f, .96f, .95f, .95f)
                : new Color(.42f, .64f, .67f, alpha);
        }
        texture.SetPixels(pixels);
        texture.Apply();
        AssetDatabase.CreateAsset(texture, path);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 256, 256), new Vector2(.5f, .5f), 100f);
        sprite.name = "RadarRings";
        AssetDatabase.AddObjectToAsset(sprite, texture);
        EditorUtility.SetDirty(texture);
        return sprite;
    }

    private static Image Panel(Scene scene, Transform parent, string name, Vector2 anchor,
        Vector2 pivot, Vector2 position, Vector2 size, Color color)
    {
        var obj = NamedObject(scene, parent, name, true);
        var rect = (RectTransform)obj.transform;
        Undo.RecordObject(rect, Islem);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        var image = Component<Image>(obj);
        Undo.RecordObject(image, Islem);
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text Label(Scene scene, Transform parent, string name, Font font, string value,
        int size, Vector2 position, Vector2 dimensions, Color color, TextAnchor align = TextAnchor.MiddleCenter)
    {
        var obj = NamedObject(scene, parent, name, true);
        var rect = (RectTransform)obj.transform;
        Undo.RecordObject(rect, Islem);
        bool left = align == TextAnchor.MiddleLeft;
        rect.anchorMin = rect.anchorMax = new Vector2(left ? 0 : .5f, .5f);
        rect.pivot = new Vector2(left ? 0 : .5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        rect.localScale = Vector3.one;
        var text = Component<Text>(obj);
        Undo.RecordObject(text, Islem);
        text.font = font;
        text.fontSize = size;
        text.fontStyle = FontStyle.Normal;
        text.alignment = align;
        text.color = color;
        text.text = value;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static GameObject RequireRoot(GameObject[] roots, string name)
    {
        var matches = roots.Where(o => o.name == name).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Tek bir " + name + " kok nesnesi gerekli.");
        return matches[0];
    }

    private static GameObject NamedObject(Scene scene, Transform parent, string name, bool ui = false)
    {
        var matches = parent == null ? scene.GetRootGameObjects().Where(o => o.name == name).ToArray()
            : parent.Cast<Transform>().Where(t => t.name == name).Select(t => t.gameObject).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("Birden fazla " + name + " bulundu.");
        if (matches.Length == 1)
        {
            if (ui && !(matches[0].transform is RectTransform))
                throw new InvalidOperationException(name + " bir UI nesnesi olmali.");
            return matches[0];
        }
        var obj = ui ? new GameObject(name, typeof(RectTransform)) : new GameObject(name);
        SceneManager.MoveGameObjectToScene(obj, scene);
        Undo.RegisterCreatedObjectUndo(obj, Islem);
        if (parent != null) Undo.SetTransformParent(obj.transform, parent, Islem);
        return obj;
    }

    private static T Component<T>(GameObject obj) where T : Component
    {
        // UnityEngine.Object can be "fake null" in the Editor; ?? bypasses that check.
        // Apply the fix to UI/Volume helpers too so a partial setup can resume safely.
        var component = obj.GetComponent<T>();
        if (component == null)
        {
            Undo.AddComponent<T>(obj);
            component = obj.GetComponent<T>();
        }
        if (component == null)
            throw new InvalidOperationException(obj.name + " uzerine " + typeof(T).Name + " eklenemedi.");
        return component;
    }

    private static Material MaterialAsset(string path, Shader shader)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
        }
        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
