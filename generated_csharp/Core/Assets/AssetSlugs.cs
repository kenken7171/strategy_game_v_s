// =============================================================================
//  ChronicleKnights — Core/Assets/AssetSlugs.cs
// -----------------------------------------------------------------------------
//  画像アセットのファイル名スラッグ（snake_case）の SoT。enum（章 EpochId / 敵原型
//  EnemyArchetype）から、配置パスに使う ASCII スラッグへの写像だけを担う純粋関数。
//
//  ★ なぜ Core（純粋層）に置くか:
//    実際の読み込み（ResourceLoader / Image）は Godot 依存のため UserInterface 側の
//    ローダ（BackgroundTextureLibrary / EnemyTextureLibrary）に置く。しかし「どの enum が
//    どのファイル名に対応するか」という対応表は Godot を一切必要としない純粋な文字列写像
//    なので、ここへ切り出して xUnit で検証可能にする（＝「画像が当てはまるか」をテストで保証）。
//
//  ★ 命名規約（docs/ASSET_MANIFEST.md と一致）:
//    - 章背景 : res://Assets/Textures/Backgrounds/{ForEpoch(epoch)}.png
//    - 敵     : res://Assets/Textures/Enemies/{ForEnemy(archetype)}.png
//    スラッグは enum 名の snake_case（例: EternalSovereign -> "eternal_sovereign"）。
//
//  開発憲法①: 識別子・スラッグ・コメント記号は ASCII のみ。
// =============================================================================

using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Chronicle;

namespace ChronicleKnights.Core.Assets;

/// <summary>画像アセットのファイル名スラッグ（snake_case）を enum から解決する純粋写像。</summary>
public static class AssetSlugs
{
    /// <summary>
    /// 章（Epoch）→ 戦場背景のファイル名スラッグ。未知 enum は空文字（呼び出し側で null 扱い）。
    /// </summary>
    public static string ForEpoch(EpochId epoch) => epoch switch
    {
        EpochId.Dawn     => "dawn",
        EpochId.Upheaval => "upheaval",
        EpochId.Decline  => "decline",
        EpochId.Twilight => "twilight",
        _                => string.Empty,
    };

    /// <summary>
    /// 敵原型（EnemyArchetype）→ 敵イラストのファイル名スラッグ。未知 enum は空文字。
    /// </summary>
    public static string ForEnemy(EnemyArchetype archetype) => archetype switch
    {
        EnemyArchetype.TrialGuardian     => "trial_guardian",
        EnemyArchetype.DawnWarden        => "dawn_warden",
        EnemyArchetype.UpheavalConqueror => "upheaval_conqueror",
        EnemyArchetype.DeclineTyrant     => "decline_tyrant",
        EnemyArchetype.EternalSovereign  => "eternal_sovereign",
        _                                => string.Empty,
    };
}
