using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Editor klasorunde kalmali; oyuncu derlemesine dahil edilmez.
public static class Rota2SahneOlusturucu
{
    private const string ModelPath = "Assets/Models/Nu_D36.fbx";

    [MenuItem("Hangar 01/Rota2 Sahnesini Olustur")]
    public static void Olustur()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
            throw new InvalidOperationException("Ucak modeli bulunamadi: " + ModelPath);

        // Validate before closing or changing any scene.
        FindMesh(model, "Nu_D36");
        FindMesh(model, "Pervane");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            throw new InvalidOperationException("Universal Render Pipeline/Lit shader bulunamadi.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Once Play modundan cikin.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EnsureFolder("Assets/Generated");
        // Each run gets its own folder; existing scenes/assets are never overwritten.
        string folder = AssetDatabase.GenerateUniqueAssetPath("Assets/Generated/Rota2");
        AssetDatabase.CreateFolder("Assets/Generated", Path.GetFileName(folder));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.GetComponent<Camera>();
        camera.transform.position = new Vector3(16, 12, -22);
        camera.transform.LookAt(new Vector3(1, 4, 2));
        camera.fieldOfView = 48;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 500;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.28f, .43f, .59f);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

        var lightObject = new GameObject("Directional Light", typeof(Light));
        var light = lightObject.GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 2;
        light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(45, -35, 0);
        RenderSettings.sun = light;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.5f, .55f, .65f);

        var volume = new GameObject("Global Volume", typeof(Volume)).GetComponent<Volume>();
        volume.isGlobal = true;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, folder + "/Rota2Volume.asset");
        var tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.ACES);
        AssetDatabase.AddObjectToAsset(tonemapping, profile);
        EditorUtility.SetDirty(profile);
        volume.sharedProfile = profile;

        // Instantiate a detached copy. Imported mesh/material source assets remain intact.
        var instance = UnityEngine.Object.Instantiate(model);
        var body = FindMesh(instance, "Nu_D36").gameObject;
        var prop = FindMesh(instance, "Pervane").gameObject;
        body.transform.SetParent(null, true);
        prop.transform.SetParent(body.transform, true); // preserve the imported pivot
        if (instance != body)
            UnityEngine.Object.DestroyImmediate(instance);
        body.name = "Nu_D36";
        prop.name = "Pervane";
        body.transform.position = new Vector3(0, 20, 0);
        if (prop.GetComponent<PervaneDondur>() == null)
            prop.AddComponent<PervaneDondur>();

        // Native URP materials also work when the FBX importer created legacy shaders.
        var materialCopies = new System.Collections.Generic.Dictionary<Material, Material>();
        foreach (var renderer in body.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.sharedMaterials = renderer.sharedMaterials.Select(source =>
            {
                if (source == null)
                    return CreateMaterial(folder, "MissingMaterial", Color.gray, shader);
                if (!materialCopies.TryGetValue(source, out var copy))
                {
                    Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor")
                        : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.gray;
                    copy = CreateMaterial(folder, "Ucak_" + materialCopies.Count, color, shader);
                    materialCopies.Add(source, copy);
                }
                return copy;
            }).ToArray();
        }

        var balloon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        balloon.name = "Balon";
        balloon.transform.position = new Vector3(40, 30, 60);
        balloon.transform.localScale = new Vector3(6, 6, 6);
        balloon.GetComponent<MeshRenderer>().sharedMaterial =
            CreateMaterial(folder, "Balon", new Color(.95f, .5f, .07f), shader);
        var hedefeGit = body.GetComponent<HedefeGit>();
        if (hedefeGit == null)
            hedefeGit = body.AddComponent<HedefeGit>();
        hedefeGit.hedef = balloon.transform;

        var gun = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        gun.name = "Ucaksavar";
        gun.transform.position = new Vector3(6, .5f, -2);
        var radar = gun.GetComponent<Radar>();
        if (radar == null)
            radar = gun.AddComponent<Radar>();
        radar.hedef = body.transform;
        // Bake base size into its own mesh so Namlu inherits unit scale.
        var baseMesh = UnityEngine.Object.Instantiate(gun.GetComponent<MeshFilter>().sharedMesh);
        baseMesh.name = "UcaksavarTaban";
        baseMesh.vertices = baseMesh.vertices.Select(v => new Vector3(v.x * 1.6f, v.y * .5f, v.z * 1.6f)).ToArray();
        baseMesh.RecalculateBounds();
        baseMesh.RecalculateNormals();
        AssetDatabase.CreateAsset(baseMesh, folder + "/UcaksavarTaban.asset");
        gun.GetComponent<MeshFilter>().sharedMesh = baseMesh;
        UnityEngine.Object.DestroyImmediate(gun.GetComponent<Collider>());
        gun.AddComponent<BoxCollider>().size = new Vector3(1.6f, 1, 1.6f);
        var gunMaterial = CreateMaterial(folder, "Ucaksavar", new Color(.15f, .22f, .12f), shader);
        gun.GetComponent<MeshRenderer>().sharedMaterial = gunMaterial;

        var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        barrel.name = "Namlu";
        barrel.transform.SetParent(gun.transform, false);
        barrel.transform.localPosition = new Vector3(0, .5f, 0);
        barrel.transform.localRotation = Quaternion.Euler(-30, 0, 0);
        var barrelMesh = UnityEngine.Object.Instantiate(barrel.GetComponent<MeshFilter>().sharedMesh);
        barrelMesh.name = "NamluMesh";
        // Local +Z points out of the barrel; origin sits at its base for aiming.
        barrelMesh.vertices = barrelMesh.vertices.Select(v => new Vector3(v.x * .22f, -v.z * .22f, (v.y + 1) * 1.4f)).ToArray();
        barrelMesh.RecalculateNormals();
        barrelMesh.RecalculateBounds();
        AssetDatabase.CreateAsset(barrelMesh, folder + "/Namlu.asset");
        barrel.GetComponent<MeshFilter>().sharedMesh = barrelMesh;
        barrel.GetComponent<MeshRenderer>().sharedMaterial = gunMaterial;
        UnityEngine.Object.DestroyImmediate(barrel.GetComponent<Collider>());
        var barrelCollider = barrel.AddComponent<BoxCollider>();
        barrelCollider.center = new Vector3(0, 0, 1.4f);
        barrelCollider.size = new Vector3(.22f, .22f, 2.8f);

        YatayOlarakCevir(gun.transform, body.transform);
        ValidateHierarchy(scene);
        AssetDatabase.SaveAssets();
        string scenePath = folder + "/Rota2.unity";
        if (!EditorSceneManager.SaveScene(scene, scenePath))
            throw new IOException("Sahne kaydedilemedi: " + scenePath);
        Selection.activeGameObject = body;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();
        Debug.Log("Rota2 hazir; hiyerarsi kontrolu PASS: " + scenePath);
    }

    [MenuItem("Hangar 01/Ucaksavari Ucaga Cevir")]
    public static void UcaksavariUcagaCevir()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Once Play modundan cikin.");
            return;
        }

        // Search only the active scene, never objects in other loaded scenes.
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.name != "Rota2")
        {
            Debug.LogWarning("Once Rota2 sahnesini aktif hale getirin.");
            return;
        }

        var roots = scene.GetRootGameObjects();
        var aircraft = roots.Where(o => o.name == "Nu_D36").ToArray();
        var guns = roots.Where(o => o.name == "Ucaksavar").ToArray();
        if (aircraft.Length != 1 || guns.Length != 1)
        {
            Debug.LogWarning("Aktif Rota2 sahnesinde tam bir Nu_D36 ve bir Ucaksavar kok nesnesi olmali.");
            return;
        }

        if (!YatayOlarakCevir(guns[0].transform, aircraft[0].transform, true))
        {
            Debug.LogWarning("Ucak ve ucaksavar ayni yatay konumda; donus degistirilmedi.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        SceneView.RepaintAll();
        Debug.Log("Ucaksavar yalnizca Y ekseninde ucaga cevrildi. Sahne kaydedilmedi.", guns[0]);
    }

    [MenuItem("Hangar 01/Takip Kamerasini Kur")]
    public static void TakipKamerasiniKur()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Takip kamerasini kurmadan once Play modundan cikin.");
            return;
        }

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.name != "Rota2")
        {
            Debug.LogWarning("Once Rota2 sahnesini aktif hale getirin.");
            return;
        }

        var roots = scene.GetRootGameObjects();
        var aircraft = roots.Where(o => o.name == "Nu_D36").ToArray();
        // Include nested/inactive cameras so a camera parented to the plane is found.
        var cameras = roots.SelectMany(o => o.GetComponentsInChildren<Camera>(true))
            .Where(c => c.name == "Main Camera").ToArray();
        if (aircraft.Length != 1 || cameras.Length != 1)
        {
            Debug.LogWarning("Aktif Rota2 sahnesinde bir Nu_D36 kok nesnesi ve bir Main Camera olmali.");
            return;
        }

        var ucak = aircraft[0].transform;
        var kamera = cameras[0].transform;
        const string islem = "Takip Kamerasini Kur";
        Undo.IncrementCurrentGroup();
        int undoGrubu = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(islem);

        var takip = kamera.GetComponent<UcakTakipKamerasi>();
        if (takip == null)
            takip = Undo.AddComponent<UcakTakipKamerasi>(kamera.gameObject);
        Undo.RecordObject(takip, islem);
        takip.HedefAta(ucak);
        takip.enabled = true;

        // Undo.SetTransformParent preserves the world transform during detachment.
        if (kamera.IsChildOf(ucak))
            Undo.SetTransformParent(kamera, null, islem);

        // Refresh existing components too: serialized values retain old defaults.
        var ayarlar = new SerializedObject(takip);
        ayarlar.FindProperty("distance").floatValue = 13f;
        ayarlar.FindProperty("height").floatValue = 4.5f;
        ayarlar.FindProperty("lookAhead").floatValue = 2.5f;
        ayarlar.FindProperty("pozisyonSmoothTime").floatValue = 0.25f;
        ayarlar.FindProperty("donusYumusakligi").floatValue = 2.5f;
        ayarlar.ApplyModifiedProperties();
        Undo.RecordObject(cameras[0], islem);
        cameras[0].fieldOfView = 65f;

        Vector3 yatayForward = ucak.forward;
        yatayForward.y = 0f;
        if (yatayForward.sqrMagnitude < 0.001f)
            yatayForward = Vector3.forward;
        yatayForward.Normalize();
        Undo.RecordObject(kamera, islem);
        kamera.position = ucak.position - yatayForward * 13f + Vector3.up * 4.5f;
        Vector3 bakisYonu = ucak.position + yatayForward * 2.5f + Vector3.up * 1.2f - kamera.position;
        if (bakisYonu.sqrMagnitude > 0.001f)
            kamera.rotation = Quaternion.LookRotation(bakisYonu.normalized, Vector3.up);

        if (PrefabUtility.IsPartOfPrefabInstance(takip))
            PrefabUtility.RecordPrefabInstancePropertyModifications(takip);
        if (PrefabUtility.IsPartOfPrefabInstance(kamera))
            PrefabUtility.RecordPrefabInstancePropertyModifications(kamera);
        if (PrefabUtility.IsPartOfPrefabInstance(cameras[0]))
            PrefabUtility.RecordPrefabInstancePropertyModifications(cameras[0]);
        Undo.CollapseUndoOperations(undoGrubu);
        EditorSceneManager.MarkSceneDirty(scene);
        SceneView.RepaintAll();
        Debug.Log("Takip kamerasi Nu_D36 icin kuruldu. Sahneyi kaydedip Play ile deneyin.", kamera);
    }

    [MenuItem("Hangar 01/Manuel Ucus Modunu Ac")]
    public static void ManuelUcusModunuAc()
    {
        UcusModunuDegistir(true);
    }

    [MenuItem("Hangar 01/Otomatik Hedef Moduna Don")]
    public static void OtomatikHedefModunaDon()
    {
        UcusModunuDegistir(false);
    }

    private static void UcusModunuDegistir(bool manuel)
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || scene.name != "Rota2")
        {
            Debug.LogWarning("Once Rota2 sahnesini aktif hale getirin.");
            return;
        }

        var aircraft = scene.GetRootGameObjects().Where(o => o.name == "Nu_D36").ToArray();
        if (aircraft.Length != 1)
        {
            Debug.LogWarning("Aktif Rota2 sahnesinde tam bir Nu_D36 kok nesnesi olmali.");
            return;
        }

        var body = aircraft[0];
        string islem = manuel ? "Manuel Ucus Modunu Ac" : "Otomatik Hedef Moduna Don";
        Undo.IncrementCurrentGroup();
        int undoGrubu = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(islem);

        var kontrol = body.GetComponent<UcakKontrol>();
        if (manuel && kontrol == null)
            kontrol = Undo.AddComponent<UcakKontrol>(body);
        if (kontrol != null)
        {
            Undo.RecordObject(kontrol, islem);
            kontrol.enabled = manuel;
            if (PrefabUtility.IsPartOfPrefabInstance(kontrol))
                PrefabUtility.RecordPrefabInstancePropertyModifications(kontrol);
        }

        var hedefeGit = body.GetComponent<HedefeGit>();
        if (hedefeGit != null)
        {
            Undo.RecordObject(hedefeGit, islem);
            hedefeGit.enabled = !manuel;
            if (PrefabUtility.IsPartOfPrefabInstance(hedefeGit))
                PrefabUtility.RecordPrefabInstancePropertyModifications(hedefeGit);
        }

        // Only flight-controller enabled states change; targets and transforms stay intact.
        Undo.CollapseUndoOperations(undoGrubu);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!manuel && hedefeGit == null)
            Debug.LogWarning("Manuel kontrol kapatildi; ucakta HedefeGit bulunmuyor.", body);
        else
            Debug.Log(manuel ? "Manuel ucus modu acildi." : "Otomatik hedef modu acildi.", body);
    }

    private static bool YatayOlarakCevir(Transform ucaksavar, Transform ucak, bool undoKaydi = false)
    {
        Vector3 yon = ucak.position - ucaksavar.position;
        yon.y = 0f;
        if (yon.sqrMagnitude <= 0f) return false;

        if (undoKaydi)
            Undo.RecordObject(ucaksavar, "Ucaksavari Ucaga Cevir");

        // A horizontal forward vector with world up produces yaw only (X/Z = 0).
        ucaksavar.rotation = Quaternion.LookRotation(yon.normalized);

        if (undoKaydi && PrefabUtility.IsPartOfPrefabInstance(ucaksavar))
            PrefabUtility.RecordPrefabInstancePropertyModifications(ucaksavar);
        return true;
    }

    private static MeshFilter FindMesh(GameObject root, string name)
    {
        var result = root.GetComponentsInChildren<MeshFilter>(true)
            .SingleOrDefault(f => f.name == name && f.sharedMesh != null);
        if (result == null)
            throw new InvalidOperationException("Modelde mesh nesnesi bulunamadi: " + name);
        return result;
    }

    private static Material CreateMaterial(string folder, string name, Color color, Shader shader)
    {
        var material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", .3f);
        AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".mat"));
        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static void ValidateHierarchy(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        string[] expected = { "Main Camera", "Directional Light", "Global Volume", "Nu_D36", "Balon", "Ucaksavar" };
        if (!roots.Select(o => o.name).OrderBy(n => n).SequenceEqual(expected.OrderBy(n => n)))
            throw new InvalidOperationException("Rota2 kok hiyerarsisi beklenenle uyusmuyor.");
        var body = roots.Single(o => o.name == "Nu_D36");
        var gun = roots.Single(o => o.name == "Ucaksavar");
        if (body.transform.childCount != 1 || body.transform.GetChild(0).name != "Pervane"
            || gun.transform.childCount != 1 || gun.transform.GetChild(0).name != "Namlu")
            throw new InvalidOperationException("Pervane / Namlu alt nesne hiyerarsisi uyusmuyor.");
    }
}
