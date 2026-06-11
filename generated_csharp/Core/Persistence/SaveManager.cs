// =============================================================================
//  ChronicleKnights — SaveManager.cs
// -----------------------------------------------------------------------------
//  セーブデータの実ファイル I/O 層（Godot 仮想ファイルシステム user:// 専用）。
//
//  「状態 ⇄ JSON 文字列」の純粋変換は SaveSerializer.cs が担当し、本ファイルは
//  その文字列を user:// 仮想パスへ「安全に書き込む / 読み戻す」ことに専念する。
//  Godot.FileAccess / Godot.DirAccess に依存するため、本ファイルだけは
//  xUnit テストプロジェクトのコンパイル対象から除外する（Godot 非依存層と分離）。
//
//  ★ アトミック書き込み（破損耐性）— Temp-swap + バックアップローテーション:
//    セーブ中のクラッシュ・電源断でデータが破損しないよう、以下の手順で書き込む。
//
//      1. 一時ファイル (path.tmp) に全文を書き切ってフラッシュ
//         → 途中でクラッシュしても汚れるのは .tmp だけ。本ファイルは無傷。
//      2. 既存の本ファイルをバックアップ (path.bak) へ退避（ローテーション）
//      3. 一時ファイルを本ファイルへリネーム（同一ボリューム内では実質アトミック）
//
//    ★ 不変条件: 上記のどの瞬間にクラッシュしても、
//      「本ファイルが正常」または「バックアップが正常」のどちらかが必ず成立する。
//      したがってセーブが完全に失われることはない。
//
//  ★ ロード時のフォールバック復帰:
//    LoadFromFile はまず本ファイルを試し、欠落・破損していたら自動で
//    バックアップ (path.bak) から復元を試みる。これにより「直前のセーブ中に
//    クラッシュして本ファイルが壊れた」ケースでも、1 つ前の状態へ安全に戻れる。
//
//  ★ Random は保存しない（SaveSerializer と同じ設計）。ロード時に
//    ChronicleGlobal が新しい Random を再注入する。
//
//  略称 (BDF/SDF/AB/HL) は本ファイルでも完全未使用。
// =============================================================================

using System.Collections.Generic;
using ChronicleKnights.Core.Chronicle;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Persistence;

/// <summary>
/// 純粋変換層 (SaveSerializer) と Godot 仮想ファイルシステム (user://) を橋渡しし、
/// アトミックなセーブ書き込みと破損時のバックアップ復帰を提供する静的ユーティリティ。
/// </summary>
public static class SaveManager
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>既定のセーブ先（Godot の user:// 仮想パス）。</summary>
    public const string DefaultSavePath = "user://save_data.json";

    /// <summary>書き込み途中の一時ファイルに付ける拡張子。</summary>
    private const string TempSuffix = ".tmp";

    /// <summary>1 世代前を退避するバックアップファイルに付ける拡張子。</summary>
    private const string BackupSuffix = ".bak";

    // ════════════════════════════════════════════════════════════════════════
    //  セーブ（アトミック書き込み）
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 4 つの状態を指定パス（既定 user://save_data.json）へアトミックに書き出す。
    /// 成功時 true、失敗（書き込み不可・例外）時 false。例外は外へ漏らさない。
    /// </summary>
    public static bool SaveToFile(
        string path,
        PointsEconomy economy,
        TimelineEngine timeline,
        IReadOnlyList<Unit> roster,
        IReadOnlyList<ChronicleLogEntry> chronicleLog)
    {
        try
        {
            var json = SaveSerializer.Serialize(economy, timeline, roster, chronicleLog);
            return WriteTextAtomic(path, json);
        }
        catch
        {
            return false;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ロード（本ファイル → バックアップ フォールバック）
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 指定パスのセーブデータを読み込んで状態を復元する。
    ///
    /// 復帰戦略:
    ///   1. まず本ファイル (path) を試す
    ///   2. 本ファイルが欠落・破損・パース失敗なら、バックアップ (path.bak) を試す
    ///   3. どちらも復元できなければ null
    ///
    /// これにより、直前のセーブ中クラッシュで本ファイルが壊れていても、
    /// 1 世代前のバックアップから安全に復帰できる。
    /// </summary>
    public static LoadedGameState? LoadFromFile(string path)
    {
        // 1. 本ファイルを優先
        var primary = TryLoadFrom(path);
        if (primary is not null) return primary;

        // 2. 本ファイルがダメならバックアップへフォールバック
        return TryLoadFrom(path + BackupSuffix);
    }

    /// <summary>
    /// 指定パスにセーブファイルが存在するか。本ファイル・バックアップの
    /// いずれかが存在すれば true（「つづきから」の活性判定に使う）。
    /// </summary>
    public static bool HasSaveFile(string path)
    {
        try
        {
            return Godot.FileAccess.FileExists(path)
                || Godot.FileAccess.FileExists(path + BackupSuffix);
        }
        catch
        {
            return false;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  内部: アトミック書き込み実装
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Temp-swap + バックアップローテーションでテキストをアトミックに書き込む。
    ///
    /// 手順と各時点のクラッシュ耐性:
    ///   1. path.tmp に全文書き込み + Flush
    ///      （クラッシュ → 本ファイル/バックアップは無傷。.tmp は次回上書きされる）
    ///   2. 既存の本ファイルを path.bak へコピー退避（旧 .bak は事前削除）
    ///      （クラッシュ → 本ファイルは旧バージョンのまま正常）
    ///   3. 本ファイルを削除し、path.tmp を本ファイルへリネーム
    ///      （クラッシュ → 本ファイルが無くても .bak が旧バージョンを保持）
    ///
    /// いずれの瞬間も「本ファイル正常」または「バックアップ正常」が成立する。
    /// </summary>
    private static bool WriteTextAtomic(string path, string text)
    {
        var tempPath = path + TempSuffix;
        var backupPath = path + BackupSuffix;

        // 1. 一時ファイルに完全書き込み（using で確実にクローズ → フラッシュ確定）
        using (var file = Godot.FileAccess.Open(tempPath, Godot.FileAccess.ModeFlags.Write))
        {
            if (file is null) return false;
            file.StoreString(text);
            file.Flush();
        }

        // 2. 既存の本ファイルがあればバックアップへ退避（ローテーション）
        if (Godot.FileAccess.FileExists(path))
        {
            if (Godot.FileAccess.FileExists(backupPath))
            {
                Godot.DirAccess.RemoveAbsolute(backupPath);
            }
            Godot.DirAccess.CopyAbsolute(path, backupPath);
        }

        // 3. 一時ファイルを本ファイルへ差し替え。
        //    RenameAbsolute は宛先が存在すると失敗し得るプラットフォームがあるため、
        //    先に本ファイルを削除する。この瞬間に本ファイルが消えても、手順 2 で
        //    退避済みのバックアップが旧バージョンを保持しているため安全。
        if (Godot.FileAccess.FileExists(path))
        {
            Godot.DirAccess.RemoveAbsolute(path);
        }
        var err = Godot.DirAccess.RenameAbsolute(tempPath, path);
        return err == Godot.Error.Ok;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  内部: 単一パスからの読み込み試行
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 指定パス 1 つから読み込み + デシリアライズを試みる。
    /// ファイルなし・読み取り失敗・パース失敗のいずれも null を返す（例外は外に出さない）。
    /// </summary>
    private static LoadedGameState? TryLoadFrom(string path)
    {
        try
        {
            var json = ReadText(path);
            if (string.IsNullOrEmpty(json)) return null;
            return SaveSerializer.Deserialize(json!);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Godot.FileAccess で user:// 仮想パスからテキストを読み込む。
    /// ファイルが無ければ null。
    /// </summary>
    private static string? ReadText(string path)
    {
        if (!Godot.FileAccess.FileExists(path)) return null;
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file is null) return null;
        return file.GetAsText();
    }
}
