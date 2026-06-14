// =============================================================================
//  ChronicleKnights — UserInterface/JobTextureLibrary.cs
// -----------------------------------------------------------------------------
//  ジョブ別イラスト（プレースホルダ）を ResourceLoader 経由で安全に取り出す静的ヘルパ。
//
//  ★ 安全ロード（資源欠落でも画面が落ちない）:
//    ResourceLoader.Exists で存在を確かめてから Load する。資源が無ければ null を返すだけ
//    （TextureRect 側は null を受けても空表示で安全）。アセット未配置でも実機は健全に起動する。
//
//  ★ リークフリー:
//    返した Texture2D は Godot の参照カウント資源。呼び出し側が TextureRect.Texture へ載せるだけで、
//    その TextureRect が QueueFree（台帳更地化 / _ExitTree）されると参照が解け資源も解放される。
//    本ヘルパは状態を一切持たない（純粋な静的ファクトリ）。
//
//  ★ 開発憲法①（ASCII 限定）:
//    アセットパスは res://Assets/Jobs/{JobId}.png（JobId enum 名は ASCII）。日本語を一切含まない。
// =============================================================================

using ChronicleKnights.Core.Job;
using Godot;

namespace ChronicleKnights.UserInterface;

/// <summary>ジョブ → イラスト Texture2D を ResourceLoader で安全に解決する状態なしの静的ファクトリ。</summary>
public static class JobTextureLibrary
{
    /// <summary>ジョブイラストの配置ルート（プレースホルダ。アセット未配置でも安全に null へ落ちる）。</summary>
    private const string JobTextureRoot = "res://Assets/Jobs/";

    private const string JobTextureExtension = ".png";

    /// <summary>
    /// 指定ジョブのイラストを取り出す。資源が存在しなければ（未配置・読込失敗）null を返す（番兵ガード）。
    /// </summary>
    public static Texture2D? TryLoad(JobId job)
    {
        var path = JobTextureRoot + job.ToString() + JobTextureExtension;
        if (!ResourceLoader.Exists(path))
        {
            return null;
        }

        return ResourceLoader.Load<Texture2D>(path);
    }
}
