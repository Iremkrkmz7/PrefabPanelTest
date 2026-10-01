# PrefabPanelTest

![Unity](https://img.shields.io/badge/Unity-2022.3.56f1-black?logo=unity)
![C#](https://img.shields.io/badge/C%23-Editor%20Tool-239120?logo=csharp)

**Türkçe** | [English](README.en.md)

Sahnedeki bir prefab instance'ının, kaynak prefab'a göre **hangi değerlerinin değiştiğini** tek bakışta gösteren ve seçtiğin değişiklikleri **tek tıkla Apply / Revert** etmeni sağlayan bir Unity editor paneli.

Unity'nin kendi *Overrides* menüsü sadece hangi bileşenlerin değiştiğini söyler. Bu panel ise her değişen property için **eski değer → yeni değer** farkını açıkça listeler, sahte (gürültü) override'ları gizler ve istediğin satırları seçip prefab'a yazmanı ya da geri almanı sağlar.

Örnek görünüm:

```
Prefab Instance: TestPanel                              [Refresh]
[x] Hide noise   (4 hidden)

2 objects, 3 changed properties:
[All|None]                            [Revert (2)|Apply (2)]

[x] ▼ TestPanel / Image  (1)                             [Select]
      [x] Color            ■ #FFFFFFFF  →  ■ #FF0000FF

[-] ▼ Icon / RectTransform  (2)                          [Select]
      [x] Anchored Position.X   0        →  25.5
      [ ] Size Delta.Y          100      →  64
```

## Özellikler

### Fark görüntüleme
- **Eski → yeni değer karşılaştırması:** Her override edilen property için kaynak prefab'daki değer ve sahnedeki değer yan yana gösterilir.
- **Canlı güncelleme:** Inspector'da bir değeri değiştirdiğinde, Apply/Revert yaptığında veya Undo/Redo kullandığında panel kendini yeniler. Slider sürüklerken takılmaması için yenileme saniyede ~10 kez ile sınırlandırılmıştır.
- **Renk önizlemesi:** Color property'lerinde değerin yanında küçük bir renk kutucuğu çıkar.
- **Okunabilir isimler:** `m_Items.Array.data[2].m_Name` gibi iç yollar `Items[2].Name` şeklinde gösterilir. Unity'nin iç yolunu görmek için satırın üzerine gelmen yeterli (tooltip).
- **Default override'lar gizli:** Prefab kökünde her zaman override görünen pozisyon, rotasyon, isim gibi alanlar gizlenir, tıpkı Unity'nin kendi panelinde olduğu gibi.
- **Hızlı seçim:** Her grubun yanındaki **Select** butonu ilgili objeyi Hierarchy'de seçer ve vurgular.
- **Kültürden bağımsız sayılar:** Türkçe sistemde bile `2,5` yerine `2.5` yazılır. Çok küçük değerler `0 → 0` gibi yanıltıcı görünmez.

### Gürültü filtresi (Hide noise)
Unity bazen gerçekte senin yapmadığın değişiklikleri de override olarak gösterir. **Hide noise** açıkken bunlar gizlenir ve kaç tanesinin gizlendiği yazılır. Gizlenen durumlar:

| Durum | Örnek |
|---|---|
| Adı bilinen sahte property'ler | TextMeshPro'nun iç durum bayrakları, `m_LocalEulerAnglesHint` |
| Layout tarafından yönetilen alanlar | Üst objede *Layout Group* varsa RectTransform pozisyon/boyutu; objede *Content Size Fitter* varsa boyut |
| TextMeshPro Auto Size | Auto Size açıkken Font Size'ı TMP kendisi hesaplar |
| Inspector'da gizli alanlar | Script bileşenlerindeki görünmeyen iç alanlar |
| Kayan nokta farkları | `100 → 100.000008` gibi yuvarlama hataları |
| Görünür fark yok | Ekranda aynı değere yuvarlanan değerler |

- Filtre kapatılırsa gürültü satırları **soluk** ve `(noise)` etiketiyle görünür. Neden gürültü sayıldığı tooltip'te yazar.
- Toggle'ın durumu Unity kapanıp açılsa da hatırlanır.
- Gizli satırlar asla seçili kalmaz, yani yanlışlıkla Apply/Revert edilmez.
- UI veya TextMeshPro paketi kurulu olmayan projelerde de kod sorunsuz derlenir.

### Seçimli Apply / Revert
- Her satırın yanında bir **checkbox** var. Grup başlığındaki checkbox objenin tüm satırlarını seçer (kısmen seçiliyse `-` gösterir).
- **All / None** ile hepsini seçebilir ya da bırakabilirsin.
- **Apply (n):** Seçili değişiklikleri kaynak prefab dosyasına yazar. Önce hangi prefab dosyalarının etkileneceğini gösteren bir onay penceresi açılır.
- **Revert (n):** Seçili değişiklikleri geri alır, değer prefab'daki haline döner.
- **Tek Ctrl+Z:** Toplu işlemin tamamı tek bir Undo adımıdır.
- **Güvenli:** Değiştirilemeyen prefab'lar (FBX modelleri, Packages içindekiler) `[read-only]` olarak işaretlenir ve Apply'da atlanır. Bir satır hata verirse diğerleri işlenmeye devam eder, hata Console'a yazılır.

### Desteklenen tipler

int, float, bool, string, char, enum, Color, Vector2/3/4, Vector2Int/3Int, Quaternion (Euler açı olarak), Rect, RectInt, Bounds, BoundsInt, obje referansları, AnimationCurve, Gradient, LayerMask ve dizi boyutları.

## Kullanım

1. Unity'de menüden **Tools → Prefab Override Panel**'i aç.
2. Hierarchy'de bir **prefab instance** seç (instance'ın herhangi bir alt objesini seçmek de yeterli).
3. Panel, o instance'taki tüm değişiklikleri listeler.
4. Prefab'a kaydetmek ya da geri almak istediğin satırları işaretle, **Apply** veya **Revert**'e bas.

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
| `Assets/Text (TMP).prefab` | Denemek için örnek TextMeshPro prefab'ı |
| `Assets/Scenes/SampleScene.unity` | Örnek sahne |
| `Assets/TextMesh Pro/` | Unity'nin TextMesh Pro kaynakları |

## Nasıl çalışıyor?

1. Seçili objeden `PrefabUtility.GetNearestPrefabInstanceRoot` ile instance kökü bulunur.
2. `PrefabUtility.GetObjectOverrides` ile override'ı olan bileşenler alınır.
3. Her bileşen `SerializedObject` ile gezilir. Sadece `prefabOverride` işaretli property'lere girilir ve en alttaki değişen alan bulunur.
4. Aynı property, `GetCorrespondingObjectFromSource` ile bulunan kaynak prefab'daki karşılığıyla karşılaştırılır.
5. Her fark gürültü kurallarından geçirilir. Gürültüyse nedeni kaydedilir.
6. Apply/Revert için `ApplyPropertyOverride` / `RevertPropertyOverride` kullanılır. Bir objenin bütün satırları seçiliyse tek seferde `ApplyObjectOverride` / `RevertObjectOverride` çağrılır.
