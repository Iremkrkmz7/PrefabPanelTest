

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;



public class PrefabOverridePanel : EditorWindow
{
    // -----------------------------
    // Data Model
    // -----------------------------
  
    // Tek bir property icin "kaynak prefab degeri -> instance degeri" farki.
    private class PropertyDiff
    {
        public string PropertyPath;   // Unity'nin ic yolu, orn: "m_LocalPosition.y"
        public string DisplayPath;    // Ekranda gosterilen hali, orn: "Local Position.Y"
        public string SourceValue;    // Kaynak prefab'daki deger (metin olarak)
        public string InstanceValue;  // Sahnedeki instance'taki deger (metin olarak)
        public Color? SourceColor;    // Sadece Color property'lerinde dolu (renk kutucugu icin)
        public Color? InstanceColor;
        public string NoiseReason;    // null = anlamli override; dolu = neden gurultu sayildigi
    }
 
    // Override'i olan tek bir obje (bilesen ya da GameObject) ve onun property farklari.
    private class OverrideEntry
    {
        public Object InstanceObject;
        public GameObject TargetGameObject;
        public string Header;
        public string SourceAssetPath;   // Apply edilecek prefab asset'inin dosya yolu
        public bool IsRootObject;        // Instance kokunun GameObject'i / Transform'u mu
        public bool CanApply;            // Read-only prefab'lara (FBX, Packages) apply edilemez
        public readonly List<PropertyDiff> Diffs = new List<PropertyDiff>();          // tum farklar
        public readonly List<PropertyDiff> VisibleDiffs = new List<PropertyDiff>();   // filtreden gecenler
    }
 
    private enum BulkAction { Apply, Revert }
 
    // Unity'nin "default override" saydigi, prefab instance KOKUNDE her zaman
    // override gorunen property'ler. Unity'nin kendi Overrides paneli de bunlari gizler.
    // Not: Unity surumleri arasinda bu liste degisebilir, Unity'nin dropdown'i ile karsilastir.
    private static readonly HashSet<string> RootDefaultOverrideProperties = new HashSet<string>
    {
        // GameObject
        "m_Name",
        // Transform
        "m_LocalPosition", "m_LocalRotation", "m_LocalEulerAnglesHint", "m_RootOrder",
        // RectTransform
        "m_AnchoredPosition", "m_SizeDelta", "m_AnchorMin", "m_AnchorMax", "m_Pivot"
    };
 
    // ---- Gurultu filtresi ayarlari ----
 
    // Adi bilinen, neredeyse her zaman "sahte" olan property'ler. Isimle eslestirildigi icin
    // projede o bilesen yoksa (orn: TMP kurulu degilse) hicbir zarari olmaz.
    private static readonly HashSet<string> KnownNoiseProperties = new HashSet<string>
    {
        // Transform: rotasyondan turetilen editor ipucu, kendi basina anlam tasimaz
        "m_LocalEulerAnglesHint",
        // TextMeshPro: ic durum bayraklari (metin yeniden cizilince kendiliginden degisir)
        "m_havePropertiesChanged", "m_isInputParsingRequired", "m_hasFontAssetChanged",
        // UI Graphic: cogunlukla bos kalan ic event
        "m_OnCullStateChanged"
    };
 
    // Ust objede bir Layout Group varsa, Unity bu RectTransform alanlarini her layout
    // hesaplamasinda yeniden yazar. Bu yuzden override gibi gorunurler ama "bizim" degisikligimiz degildir.
    private static readonly HashSet<string> DrivenByParentLayout = new HashSet<string>
    {
        "m_AnchoredPosition", "m_SizeDelta", "m_AnchorMin", "m_AnchorMax"
    };
 
    // Objenin kendisinde Content Size Fitter / Aspect Ratio Fitter varsa boyut otomatik hesaplanir.
    private static readonly HashSet<string> DrivenBySelfLayout = new HashSet<string>
    {
        "m_SizeDelta"
    };
 
    // UI paketine dogrudan referans vermemek icin interface'leri isimle ariyoruz (asagida aciklama var).
    private const string LayoutGroupInterface = "UnityEngine.UI.ILayoutGroup";
    private const string LayoutSelfControllerInterface = "UnityEngine.UI.ILayoutSelfController";
 
    // Bu kadar kucuk float farklari kayan nokta yuvarlama hatasidir (orn: 100 -> 100.000008)
    private const float FloatNoiseEpsilon = 1e-4f;
 
    private const string HideNoisePrefKey = "PrefabOverridePanel.HideNoise";
 
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
 
    private const int MaxValueLength = 40;
    private const float ColorSwatchSize = 12f;
 
    private readonly List<OverrideEntry> _entries = new List<OverrideEntry>();
    private readonly HashSet<Object> _collapsedObjects = new HashSet<Object>();
 
    // Checkbox secimi: hangi objenin hangi property yollari secili.
    // Liste her yenilendiginde sifirdan kuruldugu icin secimi entry'lerin disinda tutuyoruz.
    private readonly Dictionary<Object, HashSet<string>> _selectedPaths =
        new Dictionary<Object, HashSet<string>>();
    private GameObject _selectedInstanceRoot;
    private Vector2 _scrollPos;
    private bool _needsRefresh;
    private int _totalDiffCount;      // gorunen (filtreden gecen) property sayisi
    private int _hiddenNoiseCount;    // filtre yuzunden gizlenen property sayisi
    private int _visibleEntryCount;
    private bool _hideNoise = true;
 
    private GUIStyle _oldValueStyle;
    private GUIStyle _newValueStyle;
    private GUIStyle _arrowStyle;
 
    // ------------------------------------------------------------------
    // Pencere yasam dongusu
    // ------------------------------------------------------------------
 
    [MenuItem("Tools/Prefab Override Panel")]
    public static void ShowWindow()
    {
        var window = GetWindow<PrefabOverridePanel>();
        window.titleContent = new GUIContent("Prefab Overrides");
        window.minSize = new Vector2(320, 200);
    }
 
    private void OnEnable()
    {
        Selection.selectionChanged += OnSelectionChanged;
        ObjectChangeEvents.changesPublished += OnChangesPublished;
        Undo.undoRedoPerformed += MarkDirty;
 
        // Toggle'in durumu Unity kapanip acilsa da hatirlansin
        _hideNoise = EditorPrefs.GetBool(HideNoisePrefKey, true);
 
        OnSelectionChanged();
    }
 
    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
        ObjectChangeEvents.changesPublished -= OnChangesPublished;
        Undo.undoRedoPerformed -= MarkDirty;
    }
 
    // Sahnede bir sey degistiginde (Inspector'da deger degistirme, apply/revert vb.)
    // Unity bunu cagirir. Hemen yeniden hesaplamiyoruz, sadece "yenilenmeli" diye
    // isaretliyoruz. Asil is OnInspectorUpdate'te yapiliyor.
    private void OnChangesPublished(ref ObjectChangeEventStream stream) => MarkDirty();
 
    private void MarkDirty() => _needsRefresh = true;
 
    // Unity bunu saniyede ~10 kez cagirir. Slider surukleme gibi yuzlerce degisiklik
    // olayini tek bir yenilemeye indirmis oluyoruz (throttle).
    private void OnInspectorUpdate()
    {
        if (!_needsRefresh) return;
        _needsRefresh = false;
        RebuildEntries();
        Repaint();
    }
 
    private void OnSelectionChanged()
    {
        GameObject previousRoot = _selectedInstanceRoot;
        _selectedInstanceRoot = null;
 
        GameObject selected = Selection.activeGameObject;
 
        // Project penceresindeki prefab asset'lerini degil, sadece sahnedeki instance'lari ele al
        if (selected != null && !EditorUtility.IsPersistent(selected))
            _selectedInstanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(selected);
 
        // Baska bir instance'a gecildiyse eski checkbox secimleri anlamsiz
        if (_selectedInstanceRoot != previousRoot)
            _selectedPaths.Clear();
 
        RebuildEntries();
        Repaint();
    }
 
    // ------------------------------------------------------------------
    // Override toplama
    // ------------------------------------------------------------------
 
    private void RebuildEntries()
    {
        _entries.Clear();
        _totalDiffCount = 0;
        _hiddenNoiseCount = 0;
        _visibleEntryCount = 0;
 
        // Instance silinmis ya da unpack edilmis olabilir
        if (_selectedInstanceRoot == null || !PrefabUtility.IsPartOfPrefabInstance(_selectedInstanceRoot))
        {
            _selectedInstanceRoot = null;
            _selectedPaths.Clear();
            return;
        }
 
        CollectOverrides(_selectedInstanceRoot);
        UpdateVisibility();
    }
 
    // Filtreye gore her entry'nin VisibleDiffs listesini ve sayaclari yeniden hesaplar.
    // Hem liste yenilenince hem de "Hide noise" toggle'i degisince cagrilir.
    private void UpdateVisibility()
    {
        _totalDiffCount = 0;
        _hiddenNoiseCount = 0;
        _visibleEntryCount = 0;
 
        foreach (var entry in _entries)
        {
            entry.VisibleDiffs.Clear();
            foreach (var diff in entry.Diffs)
            {
                if (_hideNoise && diff.NoiseReason != null)
                    _hiddenNoiseCount++;
                else
                    entry.VisibleDiffs.Add(diff);
            }
 
            _totalDiffCount += entry.VisibleDiffs.Count;
            if (IsEntryVisible(entry)) _visibleEntryCount++;
        }
 
        // Gizlenen satirlar secili kalmasin (gorunmeyen bir sey yanlislikla apply edilmesin)
        PruneSelection();
    }
 
    // Tum farklari gurultu olan objeyi hic gosterme.
    // (Hic fark tespit edilemeyen objeyi ise gostermeye devam ediyoruz, bilgi amacli.)
    private static bool IsEntryVisible(OverrideEntry entry)
    {
        return entry.VisibleDiffs.Count > 0 || entry.Diffs.Count == 0;
    }
 
    private void CollectOverrides(GameObject instanceRoot)
    {
        List<ObjectOverride> objectOverrides =
            PrefabUtility.GetObjectOverrides(instanceRoot, includeDefaultOverrides: false);
 
        foreach (var ov in objectOverrides)
        {
            Object instanceObject = ov.instanceObject;
            GameObject owner = GetOwningGameObject(instanceObject);
            if (owner == null) continue;
 
            // Kokteki GameObject / Transform icin default override'lari eleyecegiz
            bool isRootObject = owner == instanceRoot &&
                                (instanceObject is GameObject || instanceObject is Transform);
 
            // Apply icin: degisiklik hangi prefab dosyasina yazilacak?
            Object sourceObject = PrefabUtility.GetCorrespondingObjectFromSource(instanceObject);
            string assetPath = sourceObject != null ? AssetDatabase.GetAssetPath(sourceObject) : null;
 
            var entry = new OverrideEntry
            {
                InstanceObject = instanceObject,
                TargetGameObject = owner,
                IsRootObject = isRootObject,
                SourceAssetPath = assetPath,
                CanApply = !string.IsNullOrEmpty(assetPath) &&
                           !PrefabUtility.IsPartOfImmutablePrefab(sourceObject)
            };
            CollectPropertyDiffs(instanceObject, isRootObject, GetLayoutDrivenProperties(instanceObject), entry.Diffs);
 
            // Kokte sadece default override'lar vardiysa listede bos kutu birakmayalim
            if (isRootObject && entry.Diffs.Count == 0) continue;
 
            entry.Header = $"{owner.name} / {instanceObject.GetType().Name}";
            _entries.Add(entry);
        }
    }
 
    // Instance'taki objeyi kaynak prefab'daki karsiligiyla karsilastirip
    // degisen her "yaprak" property icin bir PropertyDiff uretir.
    private static void CollectPropertyDiffs(Object instanceObject, bool isRootObject,
                                             HashSet<string> layoutDriven, List<PropertyDiff> results)
    {
        // Instance'taki objenin, kaynak prefab asset'indeki karsiligi
        Object sourceObject = PrefabUtility.GetCorrespondingObjectFromSource(instanceObject);
 
        using (var instanceSO = new SerializedObject(instanceObject))
        using (var sourceSO = sourceObject != null ? new SerializedObject(sourceObject) : null)
        {
            // Gizli alan kontrolunu sadece script bilesenlerinde (MonoBehaviour) yapiyoruz.
            // Unity'nin kendi bilesenlerinde (GameObject, Transform...) bazi onemli alanlar
            // teknik olarak "gizli" isaretli olabilir, onlari yanlislikla saklamak istemeyiz.
            HashSet<string> visiblePaths = instanceObject is MonoBehaviour
                ? CollectVisiblePaths(instanceSO)
                : null;
 
            // TMP'de "Auto Size" aciksa Font Size'i TMP kendisi hesaplar -> override'i gurultudur
            bool tmpAutoSize = false;
            if (IsTextMeshProComponent(instanceObject))
            {
                SerializedProperty autoSize = instanceSO.FindProperty("m_enableAutoSizing");
                tmpAutoSize = autoSize != null && autoSize.boolValue;
            }
 
            SerializedProperty it = instanceSO.GetIterator();
            bool enterChildren = true;
 
            while (it.Next(enterChildren))
            {
                enterChildren = false;
 
                // Bu property ve alt agaci degismemis -> tamamen atla
                if (!it.prefabOverride) continue;
 
                // Struct / dizi gibi bir kap ise: icine gir, asil degisen alt alani bul
                if (it.hasChildren && !IsAtomicType(it.propertyType))
                {
                    enterChildren = true;
                    continue;
                }
 
                if (isRootObject && IsRootDefaultOverride(it.propertyPath)) continue;
 
                // Ayni yol kaynak prefab'da olmayabilir (orn: instance'ta diziye eleman eklendiyse)
                SerializedProperty sourceProp = sourceSO?.FindProperty(it.propertyPath);
 
                var diff = new PropertyDiff
                {
                    PropertyPath = it.propertyPath,
                    DisplayPath = ToDisplayPath(it.propertyPath),
                    SourceValue = FormatValue(sourceProp),
                    InstanceValue = FormatValue(it),
                    SourceColor = TryGetColor(sourceProp),
                    InstanceColor = TryGetColor(it)
                };
                diff.NoiseReason = DetectNoise(it, sourceProp, diff, visiblePaths, layoutDriven, tmpAutoSize);
                results.Add(diff);
            }
        }
    }
 
    // Alt alanlari olsa bile tek parca gostermek istedigimiz tipler.
    // (Color'i r/g/b/a diye bolmek ya da Quaternion'un x/y/z/w'sini gostermek anlamsiz.)
    private static bool IsAtomicType(SerializedPropertyType type)
    {
        switch (type)
        {
            case SerializedPropertyType.String:
            case SerializedPropertyType.Color:
            case SerializedPropertyType.Quaternion:
            case SerializedPropertyType.AnimationCurve:
            case SerializedPropertyType.Gradient:
            case SerializedPropertyType.ObjectReference:
            case SerializedPropertyType.ExposedReference:
                return true;
            default:
                return false;
        }
    }
 
    private static bool IsRootDefaultOverride(string propertyPath)
    {
        return RootDefaultOverrideProperties.Contains(GetRootName(propertyPath));
    }
 
    // "m_LocalPosition.y" -> "m_LocalPosition"
    private static string GetRootName(string propertyPath)
    {
        int dot = propertyPath.IndexOf('.');
        return dot >= 0 ? propertyPath.Substring(0, dot) : propertyPath;
    }
 
    // ------------------------------------------------------------------
    // Gurultu tespiti
    // ------------------------------------------------------------------
 
    // Bir farkin neden "gurultu" oldugunu dondurur; anlamli bir degisiklikse null doner.
    // Kurallar ucuzdan pahaliya ve kesinden tahmine dogru siralandi.
    private static string DetectNoise(SerializedProperty instanceProp, SerializedProperty sourceProp,
                                      PropertyDiff diff, HashSet<string> visiblePaths,
                                      HashSet<string> layoutDriven, bool tmpAutoSize)
    {
        string path = instanceProp.propertyPath;
        string rootName = GetRootName(path);
 
        // 1) Adi bilinen sahte property
        if (KnownNoiseProperties.Contains(rootName))
            return "known noisy property";
 
        // 2) Layout bileseni tarafindan otomatik yazilan RectTransform alani
        if (layoutDriven != null && layoutDriven.Contains(rootName))
            return "driven by a layout component";
 
        // 2b) TextMeshPro Auto Size acikken font boyutunu TMP hesaplar
        if (tmpAutoSize && rootName == "m_fontSize")
            return "calculated by TextMeshPro Auto Size";
 
        // 3) Inspector'da gorunmeyen ic alan (sadece script bilesenlerinde kontrol ediliyor)
        if (visiblePaths != null && !IsVisiblePath(path, visiblePaths))
            return "hidden in the Inspector";
 
        if (sourceProp != null)
        {
            // 4) Kayan nokta yuvarlama farki (100 -> 100.000008 gibi)
            if (instanceProp.propertyType == SerializedPropertyType.Float &&
                sourceProp.propertyType == SerializedPropertyType.Float &&
                Mathf.Abs(instanceProp.floatValue - sourceProp.floatValue) <= FloatNoiseEpsilon)
                return "floating-point drift";
 
            // 5) Ekranda ayni gorunuyor (orn: renkler ayni hex koduna yuvarlaniyor).
            //    Obje referanslarinda uygulamiyoruz: ayni isimli iki farkli asset gercek bir degisikliktir.
            if (instanceProp.propertyType != SerializedPropertyType.ObjectReference &&
                diff.SourceValue == diff.InstanceValue)
                return "no visible difference";
        }
 
        return null;
    }
 
    // Inspector'da gorunen property yollarini toplar (NextVisible gizli alanlari atlar).
    // Dizilerin icine girmiyoruz: binlerce elemanli bir dizi her yenilemede pahali olur.
    private static HashSet<string> CollectVisiblePaths(SerializedObject so)
    {
        var paths = new HashSet<string>();
        SerializedProperty it = so.GetIterator();
        bool enterChildren = true;
 
        while (it.NextVisible(enterChildren))
        {
            paths.Add(it.propertyPath);
            enterChildren = !it.isArray;
        }
        return paths;
    }
 
    private static bool IsVisiblePath(string path, HashSet<string> visiblePaths)
    {
        if (visiblePaths.Contains(path)) return true;
 
        // "m_Items.Array.data[2].x" -> dizinin kendisi ("m_Items") gorunuyorsa elemanlari da gorunur sayariz
        int arrayIndex = path.IndexOf(".Array.");
        return arrayIndex >= 0 && visiblePaths.Contains(path.Substring(0, arrayIndex));
    }
 
    // RectTransform'un hangi alanlari bir layout bileseni tarafindan yonetiliyor?
    // Yonetilmiyorsa null doner.
    private static HashSet<string> GetLayoutDrivenProperties(Object instanceObject)
    {
        if (!(instanceObject is RectTransform rectTransform)) return null;
 
        Transform parent = rectTransform.parent;
        if (parent != null && HasEnabledComponentWithInterface(parent.gameObject, LayoutGroupInterface))
            return DrivenByParentLayout;
 
        if (HasEnabledComponentWithInterface(rectTransform.gameObject, LayoutSelfControllerInterface))
            return DrivenBySelfLayout;
 
        return null;
    }
 
    // UnityEngine.UI tiplerini dogrudan yazmak yerine interface'i ISMIYLE ariyoruz.
    // Boylece projede UI paketi olmasa bile kodumuz derlenmeye devam eder (Asset Store icin onemli).
    private static bool HasEnabledComponentWithInterface(GameObject go, string interfaceFullName)
    {
        foreach (Component component in go.GetComponents<Component>())
        {
            if (component == null) continue;                              // "Missing Script"
            if (component is Behaviour behaviour && !behaviour.enabled) continue; // kapali bilesen yonetmez
            if (component.GetType().GetInterface(interfaceFullName) != null) return true;
        }
        return false;
    }
 
    // Tum TMP bilesenleri TMP_Text'ten turer. Tip yerine ISIMLE bakiyoruz ki
    // TextMeshPro yuklu olmayan projelerde de kod derlensin.
    private static bool IsTextMeshProComponent(Object obj)
    {
        for (var type = obj.GetType(); type != null; type = type.BaseType)
        {
            if (type.Name == "TMP_Text") return true;
        }
        return false;
    }
 
    private static GameObject GetOwningGameObject(Object obj)
    {
        if (obj is GameObject go) return go;
        if (obj is Component comp) return comp.gameObject;
        return null;
    }
 
    // ------------------------------------------------------------------
    // Deger formatlama
    // ------------------------------------------------------------------
 
    // "m_Items.Array.data[2].m_Name" -> "Items[2].Name"
    private static string ToDisplayPath(string propertyPath)
    {
        string path = propertyPath
            .Replace(".Array.size", ".Size")
            .Replace(".Array.data[", "[");
 
        string[] parts = path.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            int bracket = part.IndexOf('[');
            string name = bracket >= 0 ? part.Substring(0, bracket) : part;
            string suffix = bracket >= 0 ? part.Substring(bracket) : string.Empty;
            parts[i] = ObjectNames.NicifyVariableName(name) + suffix;
        }
        return string.Join(".", parts);
    }
 
    private static string FormatValue(SerializedProperty p)
    {
        if (p == null) return "(none)";
 
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer:
                return p.longValue.ToString(Inv);
 
            case SerializedPropertyType.LayerMask:
            case SerializedPropertyType.ArraySize:
                return p.intValue.ToString(Inv);
 
            case SerializedPropertyType.Boolean:
                return p.boolValue ? "true" : "false";
 
            case SerializedPropertyType.Float:
                return FormatFloat(p.floatValue);
 
            case SerializedPropertyType.String:
                return $"\"{p.stringValue}\"";
 
            case SerializedPropertyType.Character:
                return $"'{(char)p.intValue}'";
 
            case SerializedPropertyType.Color:
                return "#" + ColorUtility.ToHtmlStringRGBA(p.colorValue);
 
            case SerializedPropertyType.ObjectReference:
            {
                Object obj = p.objectReferenceValue;
                return obj != null ? $"{obj.name} ({obj.GetType().Name})" : "None";
            }
 
            case SerializedPropertyType.Enum:
            {
                int index = p.enumValueIndex;
                string[] names = p.enumDisplayNames;
                // Flags enum'larda index -1 olabilir, o zaman ham sayiyi goster
                return index >= 0 && index < names.Length ? names[index] : p.intValue.ToString(Inv);
            }
 
            case SerializedPropertyType.Vector2:
            {
                Vector2 v = p.vector2Value;
                return FormatVector(v.x, v.y);
            }
            case SerializedPropertyType.Vector3:
            {
                Vector3 v = p.vector3Value;
                return FormatVector(v.x, v.y, v.z);
            }
            case SerializedPropertyType.Vector4:
            {
                Vector4 v = p.vector4Value;
                return FormatVector(v.x, v.y, v.z, v.w);
            }
            case SerializedPropertyType.Vector2Int:
            {
                Vector2Int v = p.vector2IntValue;
                return $"({v.x}, {v.y})";
            }
            case SerializedPropertyType.Vector3Int:
            {
                Vector3Int v = p.vector3IntValue;
                return $"({v.x}, {v.y}, {v.z})";
            }
 
            case SerializedPropertyType.Quaternion:
            {
                Vector3 e = p.quaternionValue.eulerAngles;
                return "Euler " + FormatVector(e.x, e.y, e.z);
            }
 
            case SerializedPropertyType.Rect:
            {
                Rect r = p.rectValue;
                return $"(x:{FormatFloat(r.x)}, y:{FormatFloat(r.y)}, w:{FormatFloat(r.width)}, h:{FormatFloat(r.height)})";
            }
            case SerializedPropertyType.RectInt:
            {
                RectInt r = p.rectIntValue;
                return $"(x:{r.x}, y:{r.y}, w:{r.width}, h:{r.height})";
            }
 
            case SerializedPropertyType.Bounds:
            {
                Bounds b = p.boundsValue;
                return $"center {FormatVector(b.center.x, b.center.y, b.center.z)}, " +
                       $"size {FormatVector(b.size.x, b.size.y, b.size.z)}";
            }
            case SerializedPropertyType.BoundsInt:
            {
                BoundsInt b = p.boundsIntValue;
                return $"pos ({b.x}, {b.y}, {b.z}), size ({b.size.x}, {b.size.y}, {b.size.z})";
            }
 
            case SerializedPropertyType.AnimationCurve:
            {
                AnimationCurve curve = p.animationCurveValue;
                return curve != null ? $"Curve ({curve.length} keys)" : "None";
            }
 
            case SerializedPropertyType.Gradient:
                return "(Gradient)";
 
            default:
                return $"({p.propertyType})";
        }
    }
 
    // Kultur bagimsiz float formatlama (Turkce locale'de "2,5" yerine "2.5")
    private static string FormatFloat(float value)
    {
        string text = value.ToString("0.###", Inv);
 
        // Cok kucuk ama sifir olmayan degerler "0" gorunmesin ("0 -> 0" gibi kafa karistirici diff)
        if ((text == "0" || text == "-0") && value != 0f)
            text = value.ToString("G3", Inv);
 
        return text;
    }
 
    private static string FormatVector(params float[] components)
    {
        return "(" + string.Join(", ", System.Array.ConvertAll(components, FormatFloat)) + ")";
    }
 
    private static Color? TryGetColor(SerializedProperty p)
    {
        if (p != null && p.propertyType == SerializedPropertyType.Color)
            return p.colorValue;
        return null;
    }
 
    // ------------------------------------------------------------------
    // GUI
    // ------------------------------------------------------------------
 
    private void EnsureStyles()
    {
        if (_oldValueStyle != null) return;
 
        _oldValueStyle = new GUIStyle(EditorStyles.label);
        _oldValueStyle.normal.textColor = EditorGUIUtility.isProSkin
            ? new Color(0.6f, 0.6f, 0.6f)
            : new Color(0.4f, 0.4f, 0.4f);
 
        _newValueStyle = new GUIStyle(EditorStyles.boldLabel);
 
        _arrowStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter };
    }
 
    private void OnGUI()
    {
        EnsureStyles();
 
        if (_selectedInstanceRoot == null)
        {
            EditorGUILayout.HelpBox("Select a prefab instance in the Hierarchy.", MessageType.Info);
            return;
        }
 
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Prefab Instance:", _selectedInstanceRoot.name, EditorStyles.boldLabel);
        if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(60)))
        {
            // Listeyi OnGUI icinde degistirmemek icin bir sonraki update'e birakiyoruz
            MarkDirty();
        }
        EditorGUILayout.EndHorizontal();
 
        DrawNoiseFilterToggle();
        EditorGUILayout.Space();
 
        if (_visibleEntryCount == 0)
        {
            string text = _hiddenNoiseCount > 0
                ? $"No meaningful overrides. {_hiddenNoiseCount} noisy override(s) hidden."
                : "No overrides.";
            EditorGUILayout.HelpBox(text, MessageType.None);
            return;
        }
 
        EditorGUILayout.LabelField(
            $"{_visibleEntryCount} objects, {_totalDiffCount} changed properties:",
            EditorStyles.boldLabel);
 
        DrawBulkToolbar();
 
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
        foreach (var entry in _entries)
        {
            if (IsEntryVisible(entry))
                DrawEntry(entry);
        }
        EditorGUILayout.EndScrollView();
    }
 
    private void DrawNoiseFilterToggle()
    {
        EditorGUILayout.BeginHorizontal();
 
        var content = new GUIContent("Hide noise",
            "Hides overrides that are usually not real changes: internal TextMeshPro/UI state, " +
            "layout-driven RectTransform values, hidden fields and tiny floating-point differences.");
 
        EditorGUI.BeginChangeCheck();
        bool hideNoise = EditorGUILayout.ToggleLeft(content, _hideNoise, GUILayout.Width(90));
        if (EditorGUI.EndChangeCheck())
        {
            _hideNoise = hideNoise;
            EditorPrefs.SetBool(HideNoisePrefKey, _hideNoise);
            UpdateVisibility();
            GUIUtility.ExitGUI(); // Liste degisti, bu karedeki cizimi guvenle kes
        }
 
        if (_hideNoise && _hiddenNoiseCount > 0)
            GUILayout.Label($"({_hiddenNoiseCount} hidden)", EditorStyles.miniLabel);
 
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }
 
    private void DrawEntry(OverrideEntry entry)
    {
        bool expanded = !_collapsedObjects.Contains(entry.InstanceObject);
 
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
 
        EditorGUILayout.BeginHorizontal();
 
        // Baslik checkbox'i: objenin tum satirlarini secer / birakir.
        // Satirlarin bir kismi seciliyse "-" (mixed) gosterir.
        int selectedInEntry = CountSelected(entry);
        bool allSelected = entry.VisibleDiffs.Count > 0 && selectedInEntry == entry.VisibleDiffs.Count;
        using (new EditorGUI.DisabledScope(entry.VisibleDiffs.Count == 0))
        {
            EditorGUI.showMixedValue = selectedInEntry > 0 && !allSelected;
            bool newAllSelected = EditorGUILayout.Toggle(allSelected, GUILayout.Width(16));
            EditorGUI.showMixedValue = false;
 
            if (newAllSelected != allSelected)
            {
                foreach (var diff in entry.VisibleDiffs)
                    SetSelected(entry, diff, newAllSelected);
            }
        }
 
        string label = $"{entry.Header}  ({entry.VisibleDiffs.Count})";
        if (!entry.CanApply) label += "  [read-only]";
        bool newExpanded = EditorGUILayout.Foldout(expanded, label, true);
 
        if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(50)))
        {
            Selection.activeGameObject = entry.TargetGameObject;
            EditorGUIUtility.PingObject(entry.TargetGameObject);
            // Secim degisince liste yeniden kurulabilir; bu karedeki cizimi guvenle kes
            GUIUtility.ExitGUI();
        }
        EditorGUILayout.EndHorizontal();
 
        if (newExpanded != expanded)
        {
            if (newExpanded) _collapsedObjects.Remove(entry.InstanceObject);
            else _collapsedObjects.Add(entry.InstanceObject);
        }
 
        if (newExpanded)
        {
            if (entry.VisibleDiffs.Count == 0)
            {
                EditorGUILayout.LabelField("No changed properties detected.", EditorStyles.miniLabel);
            }
            else
            {
                foreach (var diff in entry.VisibleDiffs)
                    DrawDiffRow(entry, diff);
            }
        }
 
        EditorGUILayout.EndVertical();
    }
 
    private void DrawDiffRow(OverrideEntry entry, PropertyDiff diff)
    {
        // Filtre kapaliyken gurultu satirlari da gorunur; onlari soluk ciziyoruz
        bool isNoise = diff.NoiseReason != null;
        Color previousColor = GUI.color;
        if (isNoise) GUI.color = new Color(1f, 1f, 1f, 0.5f);
 
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);
 
        bool selected = IsSelected(entry, diff);
        bool newSelected = EditorGUILayout.Toggle(selected, GUILayout.Width(16));
        if (newSelected != selected)
            SetSelected(entry, diff, newSelected);
 
        // Tooltip'te Unity'nin ic yolunu gosteriyoruz (debug icin faydali)
        float pathWidth = Mathf.Max(100f, position.width * 0.35f);
        string pathText = isNoise ? diff.DisplayPath + " (noise)" : diff.DisplayPath;
        string pathTooltip = isNoise ? $"{diff.PropertyPath}\nNoise: {diff.NoiseReason}" : diff.PropertyPath;
        GUILayout.Label(new GUIContent(pathText, pathTooltip), GUILayout.Width(pathWidth));
 
        DrawValue(diff.SourceValue, diff.SourceColor, _oldValueStyle);
        GUILayout.Label("→", _arrowStyle, GUILayout.Width(18));
        DrawValue(diff.InstanceValue, diff.InstanceColor, _newValueStyle);
 
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
 
        GUI.color = previousColor;
    }
 
    private static void DrawValue(string text, Color? color, GUIStyle style)
    {
        if (color.HasValue)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            Rect rect = GUILayoutUtility.GetRect(ColorSwatchSize, lineHeight, GUILayout.Width(ColorSwatchSize));
            rect.y += (lineHeight - ColorSwatchSize) * 0.5f;
            rect.height = ColorSwatchSize;
            EditorGUI.DrawRect(rect, color.Value);
        }
 
        // Uzun degerleri kisalt, tam halini tooltip'te goster
        string shown = text.Length > MaxValueLength
            ? text.Substring(0, MaxValueLength - 1) + "…"
            : text;
 
        GUILayout.Label(new GUIContent(shown, text), style, GUILayout.ExpandWidth(false));
    }
 
    private void DrawBulkToolbar()
    {
        int selectedCount = CountAllSelected();
 
        EditorGUILayout.BeginHorizontal();
 
        if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(40)))
            SetAllSelected(true);
        if (GUILayout.Button("None", EditorStyles.miniButtonRight, GUILayout.Width(45)))
            SetAllSelected(false);
 
        GUILayout.FlexibleSpace();
 
        using (new EditorGUI.DisabledScope(selectedCount == 0))
        {
            if (GUILayout.Button($"Revert ({selectedCount})", EditorStyles.miniButtonLeft, GUILayout.Width(85)))
            {
                RunBulkAction(BulkAction.Revert);
                GUIUtility.ExitGUI(); // Liste degisti, bu karedeki cizimi guvenle kes
            }
            if (GUILayout.Button($"Apply ({selectedCount})", EditorStyles.miniButtonRight, GUILayout.Width(85)))
            {
                ApplySelectedWithConfirmation();
                GUIUtility.ExitGUI(); // Modal dialog sonrasi da ExitGUI onerilir
            }
        }
 
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(2);
    }
 
    // ------------------------------------------------------------------
    // Checkbox secimi
    // ------------------------------------------------------------------
 
    private bool IsSelected(OverrideEntry entry, PropertyDiff diff)
    {
        return _selectedPaths.TryGetValue(entry.InstanceObject, out var paths) &&
               paths.Contains(diff.PropertyPath);
    }
 
    private void SetSelected(OverrideEntry entry, PropertyDiff diff, bool selected)
    {
        if (!_selectedPaths.TryGetValue(entry.InstanceObject, out var paths))
        {
            if (!selected) return;
            paths = new HashSet<string>();
            _selectedPaths[entry.InstanceObject] = paths;
        }
 
        if (selected) paths.Add(diff.PropertyPath);
        else paths.Remove(diff.PropertyPath);
 
        if (paths.Count == 0)
            _selectedPaths.Remove(entry.InstanceObject);
    }
 
    private int CountSelected(OverrideEntry entry)
    {
        int count = 0;
        foreach (var diff in entry.VisibleDiffs)
            if (IsSelected(entry, diff)) count++;
        return count;
    }
 
    private int CountAllSelected()
    {
        int count = 0;
        foreach (var entry in _entries)
            count += CountSelected(entry);
        return count;
    }
 
    private void SetAllSelected(bool selected)
    {
        foreach (var entry in _entries)
            foreach (var diff in entry.VisibleDiffs)
                SetSelected(entry, diff, selected);
    }
 
    // Liste yenilendikten sonra artik var olmayan secimleri (orn: geri alinmis override'lar) temizler.
    private void PruneSelection()
    {
        var stillValid = new Dictionary<Object, HashSet<string>>();
 
        foreach (var entry in _entries)
        {
            if (!_selectedPaths.TryGetValue(entry.InstanceObject, out var oldPaths)) continue;
 
            var kept = new HashSet<string>();
            foreach (var diff in entry.VisibleDiffs)
                if (oldPaths.Contains(diff.PropertyPath)) kept.Add(diff.PropertyPath);
 
            if (kept.Count > 0)
                stillValid[entry.InstanceObject] = kept;
        }
 
        _selectedPaths.Clear();
        foreach (var pair in stillValid)
            _selectedPaths.Add(pair.Key, pair.Value);
    }
 
    // ------------------------------------------------------------------
    // Toplu Apply / Revert
    // ------------------------------------------------------------------
 
    private void ApplySelectedWithConfirmation()
    {
        int selectedCount = 0;
        var assetPaths = new HashSet<string>();
 
        foreach (var entry in _entries)
        {
            int count = CountSelected(entry);
            if (count == 0 || !entry.CanApply) continue;
            selectedCount += count;
            assetPaths.Add(entry.SourceAssetPath);
        }
 
        if (assetPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("Apply Overrides",
                "None of the selected overrides can be applied (read-only prefab).", "OK");
            return;
        }
 
        // Basit onay. 7. adimda bunu daha detayli hale getirecegiz.
        string message =
            $"Apply {selectedCount} override(s) to {assetPaths.Count} prefab asset(s)?\n\n" +
            string.Join("\n", assetPaths) +
            "\n\nThis changes the prefab file and every instance that uses it.";
 
        if (!EditorUtility.DisplayDialog("Apply Overrides", message, "Apply", "Cancel"))
            return;
 
        RunBulkAction(BulkAction.Apply);
    }
 
    private void RunBulkAction(BulkAction action)
    {
        // Tum islemleri tek bir Undo adiminda topla -> tek Ctrl+Z ile hepsi geri gelir
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(action == BulkAction.Apply
            ? "Apply Prefab Overrides"
            : "Revert Prefab Overrides");
        int undoGroup = Undo.GetCurrentGroup();
 
        int done = 0, skipped = 0, failed = 0;
 
        // Islem sirasinda listeye dokunulmasin diye kopyasi uzerinde donuyoruz
        foreach (var entry in new List<OverrideEntry>(_entries))
        {
            // Sadece gorunen ve secili satirlar islenir; gizli gurultuye asla dokunulmaz
            var selectedDiffs = new List<PropertyDiff>();
            foreach (var diff in entry.VisibleDiffs)
                if (IsSelected(entry, diff)) selectedDiffs.Add(diff);
 
            if (selectedDiffs.Count == 0) continue;
 
            if (entry.InstanceObject == null)
            {
                failed += selectedDiffs.Count;
                continue;
            }
 
            if (action == BulkAction.Apply && !entry.CanApply)
            {
                skipped += selectedDiffs.Count;
                continue;
            }
 
            // Objenin TUM satirlari seciliyse tek bir obje islemi yap (tek kayit, daha hizli).
            // Kok objede yapmiyoruz: gizledigimiz default override'lari da prefab'a yazabilir.
            // Diffs (tum farklar) ile karsilastiriyoruz: gizli gurultu satiri varsa sayi tutmaz
            // ve property property gidilir, yani gizli satirlar apply/revert edilmez.
            bool wholeObject = !entry.IsRootObject && selectedDiffs.Count == entry.Diffs.Count;
 
            if (wholeObject)
            {
                bool ok = TryRun(() =>
                {
                    if (action == BulkAction.Apply)
                        PrefabUtility.ApplyObjectOverride(entry.InstanceObject, entry.SourceAssetPath, InteractionMode.UserAction);
                    else
                        PrefabUtility.RevertObjectOverride(entry.InstanceObject, InteractionMode.UserAction);
                }, entry.TargetGameObject);
 
                if (ok) done += selectedDiffs.Count;
                else failed += selectedDiffs.Count;
                continue;
            }
 
            // Dizilerde sira onemli: Revert'te once elemanlar, sonra "Array.size" geri alinmali.
            // (Once boyut kuculurse elemanlarin yolu artik bulunamaz.) Iterator sirasinda
            // size elemanlardan once geldigi icin Revert'te listeyi tersten isliyoruz.
            if (action == BulkAction.Revert)
                selectedDiffs.Reverse();
 
            foreach (var diff in selectedDiffs)
            {
                bool ok = TryRun(() => RunPropertyAction(action, entry, diff.PropertyPath),
                                 entry.TargetGameObject);
                if (ok) done++;
                else failed++;
            }
        }
 
        Undo.CollapseUndoOperations(undoGroup);
 
        string verb = action == BulkAction.Apply ? "Applied" : "Reverted";
        ShowNotification(new GUIContent($"{verb} {done} override(s)"));
 
        if (skipped > 0)
            Debug.LogWarning($"[Prefab Override Panel] Skipped {skipped} override(s) on read-only prefabs.");
        if (failed > 0)
            Debug.LogWarning($"[Prefab Override Panel] {failed} override(s) failed. See the errors above.");
 
        _selectedPaths.Clear();
        MarkDirty();
    }
 
    private static void RunPropertyAction(BulkAction action, OverrideEntry entry, string propertyPath)
    {
        // Her apply'dan sonra prefab yeniden import edilebildigi icin her seferinde
        // taze bir SerializedObject aciyoruz (eskisi guncel olmayabilir).
        using (var so = new SerializedObject(entry.InstanceObject))
        {
            SerializedProperty prop = so.FindProperty(propertyPath);
            if (prop == null)
                throw new System.InvalidOperationException($"Property not found: {propertyPath}");
 
            if (action == BulkAction.Apply)
                PrefabUtility.ApplyPropertyOverride(prop, entry.SourceAssetPath, InteractionMode.UserAction);
            else
                PrefabUtility.RevertPropertyOverride(prop, InteractionMode.UserAction);
        }
    }
 
    // Bir islem hata verirse Console'a yaz ve false don; diger islemler devam etsin.
    private static bool TryRun(System.Action operation, Object context)
    {
        try
        {
            operation();
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Prefab Override Panel] {e.Message}", context);
            return false;
        }
    }
}
#endif