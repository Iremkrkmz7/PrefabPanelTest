# PrefabPanelTest

![Unity](https://img.shields.io/badge/Unity-2022.3.56f1-black?logo=unity)
![C#](https://img.shields.io/badge/C%23-Editor%20Tool-239120?logo=csharp)

Sahnedeki bir prefab instance'ının, kaynak prefab'a göre **hangi değerlerinin değiştiğini** tek bakışta gösteren bir Unity editor paneli.

Unity'nin kendi *Overrides* menüsü sadece hangi bileşenlerin değiştiğini söyler. Bu panel ise her değişen property için **eski değer → yeni değer** farkını açıkça listeler.

Örnek görünüm:

```
Prefab Instance: TestPanel                         [Yenile]
2 obje, 3 degisen property:

▼ TestPanel / Image  (1)                              [Sec]
      Color              ■ #FFFFFFFF  →  ■ #FF0000FF

▼ Icon / RectTransform  (2)                           [Sec]
      Anchored Position.X   0        →  25.5
      Size Delta.Y          100      →  64
```

## Özellikler

- **Eski → yeni değer karşılaştırması:** Her override edilen property için kaynak prefab'daki değer ve sahnedeki değer yan yana gösterilir.
- **Canlı güncelleme:** Inspector'da bir değeri değiştirdiğinde, Apply/Revert yaptığında veya Undo/Redo kullandığında panel kendini yeniler. Slider sürüklerken takılmaması için yenileme saniyede ~10 kez ile sınırlandırılmıştır.
- **Renk önizlemesi:** Color property'lerinde değerin yanında küçük bir renk kutucuğu çıkar.
- **Okunabilir isimler:** `m_Items.Array.data[2].m_Name` gibi iç yollar `Items[2].Name` şeklinde gösterilir. Unity'nin iç yolunu görmek için satırın üzerine gelmen yeterli (tooltip).
- **Gereksiz kalabalık yok:** Prefab kökünde her zaman override görünen pozisyon, rotasyon, isim gibi *default override*'lar gizlenir, tıpkı Unity'nin kendi panelinde olduğu gibi.
- **Hızlı seçim:** Her grubun yanındaki **Sec** butonu ilgili objeyi Hierarchy'de seçer ve vurgular.
- **Açılır/kapanır gruplar:** Çok sayıda override varsa grupları katlayabilirsin.
- **Kültürden bağımsız sayılar:** Türkçe sistemde bile `2,5` yerine `2.5` yazılır. Çok küçük değerler `0 → 0` gibi yanıltıcı görünmez.

### Desteklenen tipler

int, float, bool, string, char, enum, Color, Vector2/3/4, Vector2Int/3Int, Quaternion (Euler açı olarak), Rect, RectInt, Bounds, BoundsInt, obje referansları, AnimationCurve, Gradient, LayerMask ve dizi boyutları.

## Kullanım

1. Unity'de menüden **Tools → Prefab Override Panel**'i aç.
2. Hierarchy'de bir **prefab instance** seç (instance'ın herhangi bir alt objesini seçmek de yeterli).
3. Panel, o instance'taki tüm değişiklikleri listeler.

> Project penceresindeki prefab asset'leri değil, sadece **sahnedeki instance'lar** incelenir.

## Kurulum

1. Repoyu klonla:
   ```bash
   git clone https://github.com/Iremkrkmz7/PrefabPanelTest.git
   ```
2. Unity Hub → **Add** → klonladığın klasörü seç.
3. Projeyi **Unity 2022.3.56f1** ile aç.

Sadece paneli başka bir projede kullanmak istersen `Assets/Editor/PrefabOverridePanel.cs` dosyasını o projenin herhangi bir `Editor` klasörüne kopyalaman yeterli.

## Proje yapısı

| Dosya | Açıklama |
|---|---|
| `Assets/Editor/PrefabOverridePanel.cs` | Editor paneli (tüm kod bu dosyada) |
| `Assets/TestPanel.prefab` | Denemek için örnek UI prefab'ı |
| `Assets/Scenes/SampleScene.unity` | Örnek sahne |

## Nasıl çalışıyor?

1. Seçili objeden `PrefabUtility.GetNearestPrefabInstanceRoot` ile instance kökü bulunur.
2. `PrefabUtility.GetObjectOverrides` ile override'ı olan bileşenler alınır.
3. Her bileşen `SerializedObject` ile gezilir. Sadece `prefabOverride` işaretli property'lere girilir ve en alttaki değişen alan bulunur.
4. Aynı property, `GetCorrespondingObjectFromSource` ile bulunan kaynak prefab'daki karşılığıyla karşılaştırılır.
