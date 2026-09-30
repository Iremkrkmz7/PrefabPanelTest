

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
    }
 
    // Override'i olan tek bir obje (bilesen ya da GameObject) ve onun property farklari.
    private class OverrideEntry
    {
        public Object InstanceObject;
        public GameObject TargetGameObject;
        public string Header;
        public readonly List<PropertyDiff> Diffs = new List<PropertyDiff>();
    }
 
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
 
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
 
    private const int MaxValueLength = 40;
    private const float ColorSwatchSize = 12f;
 
    private readonly List<OverrideEntry> _entries = new List<OverrideEntry>();
    private readonly HashSet<Object> _collapsedObjects = new HashSet<Object>();
    private GameObject _selectedInstanceRoot;
    private Vector2 _scrollPos;
    private bool _needsRefresh;
    private int _totalDiffCount;
 
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
        _selectedInstanceRoot = null;
 
        GameObject selected = Selection.activeGameObject;
 
        // Project penceresindeki prefab asset'lerini degil, sadece sahnedeki instance'lari ele al
        if (selected != null && !EditorUtility.IsPersistent(selected))
            _selectedInstanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(selected);
 
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
 
        // Instance silinmis ya da unpack edilmis olabilir
        if (_selectedInstanceRoot == null || !PrefabUtility.IsPartOfPrefabInstance(_selectedInstanceRoot))
        {
            _selectedInstanceRoot = null;
            return;
        }
 
        CollectOverrides(_selectedInstanceRoot);
 
        foreach (var entry in _entries)
            _totalDiffCount += entry.Diffs.Count;
    }
 
    private void CollectOverrides(GameObject instanceRoot)
    {
        var objectOverrides =
             PrefabUtility.GetObjectOverrides(instanceRoot, includeDefaultOverrides: false);
       
        foreach (var ov in objectOverrides)
        {
            Object instanceObject = ov.instanceObject;
            GameObject owner = GetOwningGameObject(instanceObject);
            if (owner == null) continue;
 
            // Kokteki GameObject / Transform icin default override'lari eleyecegiz
            bool isRootObject = owner == instanceRoot &&
                                (instanceObject is GameObject || instanceObject is Transform);
 
            var entry = new OverrideEntry
            {
                InstanceObject = instanceObject,
                TargetGameObject = owner
            };
            CollectPropertyDiffs(instanceObject, isRootObject, entry.Diffs);
 
            // Kokte sadece default override'lar vardiysa listede bos kutu birakmayalim
            if (isRootObject && entry.Diffs.Count == 0) continue;
 
            entry.Header = $"{owner.name} / {instanceObject.GetType().Name}";
            _entries.Add(entry);
        }
    }
 
    // Instance'taki objeyi kaynak prefab'daki karsiligiyla karsilastirip
    // degisen her "yaprak" property icin bir PropertyDiff uretir.
    private static void CollectPropertyDiffs(Object instanceObject, bool isRootObject, List<PropertyDiff> results)
    {
        // Instance'taki objenin, kaynak prefab asset'indeki karsiligi
        Object sourceObject = PrefabUtility.GetCorrespondingObjectFromSource(instanceObject);
 
        using (var instanceSO = new SerializedObject(instanceObject))
        using (var sourceSO = sourceObject != null ? new SerializedObject(sourceObject) : null)
        {
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
 
                results.Add(new PropertyDiff
                {
                    PropertyPath = it.propertyPath,
                    DisplayPath = ToDisplayPath(it.propertyPath),
                    SourceValue = FormatValue(sourceProp),
                    InstanceValue = FormatValue(it),
                    SourceColor = TryGetColor(sourceProp),
                    InstanceColor = TryGetColor(it)
                });
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
        // "m_LocalPosition.y" -> "m_LocalPosition"
        int dot = propertyPath.IndexOf('.');
        string rootName = dot >= 0 ? propertyPath.Substring(0, dot) : propertyPath;
        return RootDefaultOverrideProperties.Contains(rootName);
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
        if (p == null) return "(yok)";
 
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
                return curve != null ? $"Curve ({curve.length} key)" : "None";
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
            EditorGUILayout.HelpBox("Hierarchy'de bir prefab instance secin.", MessageType.Info);
            return;
        }
 
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Prefab Instance:", _selectedInstanceRoot.name, EditorStyles.boldLabel);
        if (GUILayout.Button("Yenile", EditorStyles.miniButton, GUILayout.Width(60)))
        {
            // Listeyi OnGUI icinde degistirmemek icin bir sonraki update'e birakiyoruz
            MarkDirty();
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();
 
        if (_entries.Count == 0)
        {
            EditorGUILayout.HelpBox("Override yok.", MessageType.None);
            return;
        }
 
        EditorGUILayout.LabelField(
            $"{_entries.Count} obje, {_totalDiffCount} degisen property:",
            EditorStyles.boldLabel);
 
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
        foreach (var entry in _entries)
            DrawEntry(entry);
        EditorGUILayout.EndScrollView();
    }
 
    private void DrawEntry(OverrideEntry entry)
    {
        bool expanded = !_collapsedObjects.Contains(entry.InstanceObject);
 
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
 
        EditorGUILayout.BeginHorizontal();
        bool newExpanded = EditorGUILayout.Foldout(
            expanded, $"{entry.Header}  ({entry.Diffs.Count})", true);
 
        if (GUILayout.Button("Sec", EditorStyles.miniButton, GUILayout.Width(40)))
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
            if (entry.Diffs.Count == 0)
            {
                EditorGUILayout.LabelField("Degisen property tespit edilemedi.", EditorStyles.miniLabel);
            }
            else
            {
                foreach (var diff in entry.Diffs)
                    DrawDiffRow(diff);
            }
        }
 
        EditorGUILayout.EndVertical();
    }
 
    private void DrawDiffRow(PropertyDiff diff)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(14);
 
        // Tooltip'te Unity'nin ic yolunu gosteriyoruz (debug icin faydali)
        float pathWidth = Mathf.Max(100f, position.width * 0.35f);
        GUILayout.Label(new GUIContent(diff.DisplayPath, diff.PropertyPath), GUILayout.Width(pathWidth));
 
        DrawValue(diff.SourceValue, diff.SourceColor, _oldValueStyle);
        GUILayout.Label("→", _arrowStyle, GUILayout.Width(18));
        DrawValue(diff.InstanceValue, diff.InstanceColor, _newValueStyle);
 
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
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
}
#endif
 