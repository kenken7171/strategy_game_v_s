// =============================================================================
//  ChronicleKnights.Tests — AssetSlugsTests.cs
// -----------------------------------------------------------------------------
//  画像アセットのスラッグ写像（AssetSlugs）と「配置との突合」を固定するテスト。
//
//  目的（旅団長の要望「画像を当てはめれることを確認」の自動化）:
//    1. スラッグ写像 : 各 enum が docs/ASSET_MANIFEST.md と一致する snake_case を返す。
//    2. 配置の突合   : 全 EpochId / EnemyArchetype について、ローダが組み立てる
//       パス（Backgrounds/{slug}.png・Enemies/{slug}.png）に実ファイルが存在する。
//       → 「画像を置けば、ローダが同じパスを引いて当てはまる」ことをビルドで保証する。
//
//  ※ ローダ本体（BackgroundTextureLibrary / EnemyTextureLibrary）は Godot 依存のため
//    xUnit からは叩けないが、パス組み立ての核（スラッグ）と配置の有無はここで担保する。
// =============================================================================

using System;
using System.IO;
using System.Linq;
using ChronicleKnights.Core.Assets;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Chronicle;
using Xunit;

namespace ChronicleKnights.Tests.Core.Assets;

public class AssetSlugsTests
{
    // ─── 1. スラッグ写像（ASSET_MANIFEST と一致） ─────────────────────────────

    [Theory]
    [InlineData(EpochId.Dawn, "dawn")]
    [InlineData(EpochId.Upheaval, "upheaval")]
    [InlineData(EpochId.Decline, "decline")]
    [InlineData(EpochId.Twilight, "twilight")]
    public void ForEpoch_ReturnsManifestSlug(EpochId epoch, string expected)
        => Assert.Equal(expected, AssetSlugs.ForEpoch(epoch));

    [Theory]
    [InlineData(EnemyArchetype.TrialGuardian, "trial_guardian")]
    [InlineData(EnemyArchetype.DawnWarden, "dawn_warden")]
    [InlineData(EnemyArchetype.UpheavalConqueror, "upheaval_conqueror")]
    [InlineData(EnemyArchetype.DeclineTyrant, "decline_tyrant")]
    [InlineData(EnemyArchetype.EternalSovereign, "eternal_sovereign")]
    public void ForEnemy_ReturnsManifestSlug(EnemyArchetype archetype, string expected)
        => Assert.Equal(expected, AssetSlugs.ForEnemy(archetype));

    [Fact]
    public void EverySlug_IsNonEmpty_ForDefinedEnumValues()
    {
        Assert.All(Enum.GetValues<EpochId>(), e => Assert.NotEqual(string.Empty, AssetSlugs.ForEpoch(e)));
        Assert.All(Enum.GetValues<EnemyArchetype>(), a => Assert.NotEqual(string.Empty, AssetSlugs.ForEnemy(a)));
    }

    // ─── 2. 配置の突合（ローダが引くパスに実ファイルがある） ─────────────────────

    [Fact]
    public void EveryEpoch_HasBackgroundImageAtLoaderPath()
    {
        var root = LocateTexturesRoot();
        foreach (var epoch in Enum.GetValues<EpochId>())
        {
            var path = Path.Combine(root, "Backgrounds", $"{AssetSlugs.ForEpoch(epoch)}.png");
            Assert.True(File.Exists(path), $"missing background for {epoch}: {path}");
        }
    }

    [Fact]
    public void EveryEnemyArchetype_HasImageAtLoaderPath()
    {
        var root = LocateTexturesRoot();
        foreach (var archetype in Enum.GetValues<EnemyArchetype>())
        {
            var path = Path.Combine(root, "Enemies", $"{AssetSlugs.ForEnemy(archetype)}.png");
            Assert.True(File.Exists(path), $"missing enemy art for {archetype}: {path}");
        }
    }

    /// <summary>
    /// テスト実行ディレクトリから上方向へ歩いて Assets/Textures を見つける（出力レイアウト非依存）。
    /// res://Assets/Textures/ = generated_csharp/Assets/Textures/ に対応。
    /// </summary>
    private static string LocateTexturesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Assets", "Textures");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Assets/Textures not found above " + AppContext.BaseDirectory);
    }
}
