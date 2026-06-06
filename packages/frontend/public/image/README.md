# ジョブアイコン アセット配置

UnitIcon コンポーネントが参照するパス規則:

```
public/image/{jobId}/{gender}.png
```

| jobId | 配置すべきファイル |
|---|---|
| iron_wall_knight | `iron_wall_knight/male.png`, `iron_wall_knight/female.png` |
| heavy_infantry   | `heavy_infantry/male.png`, `heavy_infantry/female.png` |
| standard_bearer  | `standard_bearer/male.png`, `standard_bearer/female.png` |
| tactician        | `tactician/male.png`, `tactician/female.png` |
| medic            | `medic/male.png`, `medic/female.png` |
| sniper           | `sniper/male.png`, `sniper/female.png` |
| sorcerer         | `sorcerer/male.png`, `sorcerer/female.png` |
| scout            | `scout/male.png`, `scout/female.png` |

合計 8 ジョブ × 2 性別 = **16 ファイル**。

## アセット仕様

- **16-bit ドット絵**（例: 32×32 or 48×48 px）
- 透過 PNG 推奨
- ファイル名は **小文字必須**（`Male.png` は NG、`male.png`）

`image-rendering: pixelated` が CSS で適用されているため、原寸の倍率で拡大表示してもエッジがぼやけません。

## 画像が無い場合の挙動

UnitIcon は `onError` で broken state に遷移し、円形のジョブ頭文字フォールバック（例: 「鉄」「狙」）を表示します。アセット未配置でもアプリは動作します。
