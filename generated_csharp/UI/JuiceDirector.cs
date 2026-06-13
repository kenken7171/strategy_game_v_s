// =============================================================================
//  ChronicleKnights — JuiceDirector.cs
// -----------------------------------------------------------------------------
//  手触り演出（ジュース）の規律を一手に引き受ける、完全無状態の静的ヘルパ。
//
//  ★ 役割と設計憲法③（単一 SoT・状態を持たない）への準拠:
//    本クラスはゲーム状態（SoT）を一切保持しない。各メソッドは「対象ノード」と
//    「演出パラメタ」を受け取り、ワンショット or ループの Tween を 1 本生成して
//    返すだけの純粋なファクトリである。演出中フラグ・台帳・カウンタの類は持たない。
//    台帳が必要な演出（カメラシェイク・HP ドレインのように永続ノードへ複数回焚く
//    もの）は、呼び出し側（BattleUI 等）が返却された Tween ハンドルを 1 本だけ保持し、
//    新演出の前に Kill する規律を負う（本クラスはその規律を「焚く」一手に集約する）。
//
//  ★ ライフサイクル安全（メモリリーク / Tween ゾンビ化の構造的根絶 — Req 4）:
//    すべての Tween は対象ノードへ Node.CreateTween でバインドする。対象ノードが
//    QueueFree されると、その Tween は Godot 側で自動失効する（明示 Kill は不要）。
//    さらにコールバックを伴う自己崩壊型（RisingPopup）では、コールバック内で必ず
//    GodotObject.IsInstanceValid ガードを通してから QueueFree し、親の cascade 解放が
//    先に走った場合でも二重解放・null 参照を起こさない。全公開メソッドは入口で
//    null / IsInstanceValid を検査し、無効ノードに対しては安全に no-op（null 返し）する。
//
//  ★ Godot コンテナとの非干渉について:
//    - Modulate / SelfModulate / Scale はコンテナのレイアウト（position / size）に
//      管理されないため、コンテナの子に対しても安全にアニメートできる。
//    - Position はコンテナが sort 時に上書きする。SlideTo はレイアウトが確定した
//      （= sort 済みの）ノードに対して呼ぶ前提で、確定後は短時間のうちに再 sort が
//      走らない静止区間を利用してワンショットで滑走させる（呼び出し側が 1 フレーム
//      待ってから呼ぶ規律を守ることで安定する）。
//
//  ★ 設計憲法①（日本語データ名のハードコード禁止）について:
//    本クラスは表示テキストを生成しない。RisingPopup に載る文字列・testid は
//    すべて呼び出し側が組み立てて渡す（数値・chrome 記号・ASCII 機械生成 testid）。
//    したがって本クラス自身は「データ名」を一切内包しない。
// =============================================================================

using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// 手触り演出（フラッシュ・シェイク・滑走・パンチ・死亡フェード・HP ドレイン・
/// 自己崩壊ポップアップ）を生成する無状態の静的ファクトリ。ゲーム状態（SoT）を
/// 一切持たず、各メソッドは Tween を 1 本作って返すだけに徹する。
/// </summary>
public static class JuiceDirector
{
    // ─── フラッシュ（modulate を一瞬染め、白＝恒等へワンショットで戻す） ──────

    /// <summary>
    /// ノードの modulate を即座に <paramref name="flashColor"/> へ染め、白（恒等）へ
    /// ワンショットでフェードして戻す。modulate は self_modulate（危険脈動等が使う別
    /// プロパティ）と独立なので、脈動アニメーションと衝突しない。
    /// 対象ノードが次の再描画で QueueFree される際、この Tween も自動失効する。
    /// </summary>
    public static Tween? Flash(Control? node, Color flashColor, double seconds)
    {
        if (node is null || !GodotObject.IsInstanceValid(node))
        {
            return null;
        }

        node.Modulate = flashColor;
        var tween = node.CreateTween();
        tween.TweenProperty(node, "modulate", Colors.White, seconds)
             .SetTrans(Tween.TransitionType.Quad)
             .SetEase(Tween.EaseType.Out);
        return tween;
    }

    // ─── カメラシェイク（永続ノードの position を減衰往復させ Zero へ収束） ────

    /// <summary>
    /// 対象ノードの position を減衰しながら数回往復させ「画面全体の揺れ」を焚く。
    /// 単発（SetLoops なし）の Tween なので最後のジョルトを終えると自動完了・破棄される。
    /// 最後は必ず基準位置（Vector2.Zero）へ収束する。
    ///
    /// ★ 多重起動・位置ドリフトの防止は呼び出し側の責務:
    ///   本メソッドは開始時に position を Zero へ戻すが、対象が永続ノードの場合は
    ///   呼び出し側が直近 1 本のハンドルを保持し、新シェイク前に Kill する規律を負う。
    /// ★ コンテナ非干渉: 対象は非コンテナ親を持つノードであること（コンテナ配下だと
    ///   sort に position を奪われる）。本プロジェクトでは盤面ルートが対象。
    /// </summary>
    public static Tween? Shake(Control? node, float amplitude, double stepSeconds)
    {
        if (node is null || !GodotObject.IsInstanceValid(node))
        {
            return null;
        }

        // 減衰する 4 ジョルト → 最後に基準（Zero）へ収束。係数で揺れ幅を徐々に絞る。
        var offsets = new[]
        {
            new Vector2(amplitude,          -amplitude * 0.60f),
            new Vector2(-amplitude * 0.80f,  amplitude * 0.50f),
            new Vector2(amplitude * 0.55f,   amplitude * 0.35f),
            new Vector2(-amplitude * 0.35f, -amplitude * 0.25f),
            Vector2.Zero,
        };

        node.Position = Vector2.Zero;
        var tween = node.CreateTween();
        foreach (var offset in offsets)
        {
            tween.TweenProperty(node, "position", offset, stepSeconds)
                 .SetTrans(Tween.TransitionType.Sine)
                 .SetEase(Tween.EaseType.InOut);
        }

        return tween;
    }

    // ─── 滑走（旧 global 位置から、確定済みの現在レイアウト位置へ position 補間） ──

    /// <summary>
    /// レイアウト確定済みのノードを、いったん <paramref name="fromGlobalPosition"/>
    /// （直前フレームでの旧 global 位置）へ瞬間移動させてから、本来のレイアウト位置へ
    /// position 補間で滑り込ませる。ローテーション後の「分隊が新しい場所へ滑る」演出に使う。
    ///
    /// ★ 前提: 呼び出し側は盤面再構築後に 1 フレーム待ち、コンテナの sort が完了して
    ///   ノードが最終位置を報告できる状態にしてから本メソッドを呼ぶこと。確定後は短い
    ///   滑走区間に再 sort が走らないため、ワンショット Tween が安定して可視化される。
    /// </summary>
    public static Tween? SlideTo(Control? node, Vector2 fromGlobalPosition, double seconds)
    {
        if (node is null || !GodotObject.IsInstanceValid(node))
        {
            return null;
        }

        // 確定済みのレイアウト位置（ローカル）が滑走の終点。現在の global との差分から、
        // 旧 global に対応するローカル始点を逆算し、そこへ一旦ワープしてから補間で戻す。
        var destinationLocal = node.Position;
        var deltaToOld = fromGlobalPosition - node.GlobalPosition;
        node.Position = destinationLocal + deltaToOld;

        var tween = node.CreateTween();
        tween.TweenProperty(node, "position", destinationLocal, seconds)
             .SetTrans(Tween.TransitionType.Cubic)
             .SetEase(Tween.EaseType.Out);
        return tween;
    }

    // ─── パンチ（scale を 1 → ピーク → 1 へ。予備動作 / 着弾の強調） ──────────

    /// <summary>
    /// ノードを中心ピボットで <paramref name="peakScale"/> 倍まで一瞬膨らませ、等倍へ戻す
    /// スケールパンチ。敵カードの「攻撃の予備動作（ワインドアップ）／着弾の手応え」に使う。
    /// scale はコンテナのレイアウトに管理されないため、コンテナ配下のノードにも焚ける。
    /// </summary>
    public static Tween? Punch(Control? node, float peakScale, double seconds)
    {
        if (node is null || !GodotObject.IsInstanceValid(node))
        {
            return null;
        }

        // 中心を基準にスケールさせる（左上基準だと膨張方向が偏るため）。
        node.PivotOffset = node.Size * 0.5f;
        node.Scale = Vector2.One;

        var tween = node.CreateTween();
        tween.TweenProperty(node, "scale", new Vector2(peakScale, peakScale), seconds * 0.35)
             .SetTrans(Tween.TransitionType.Back)
             .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "scale", Vector2.One, seconds * 0.65)
             .SetTrans(Tween.TransitionType.Back)
             .SetEase(Tween.EaseType.In);
        return tween;
    }

    // ─── 死亡フェード（modulate を低アルファのセピアへ沈める。英霊の退場） ──────

    /// <summary>
    /// ノードの modulate を <paramref name="deathTint"/>（低アルファのセピア調）へ
    /// ワンショットで沈め、撃破された英霊が「褪せて退場する」気配を物質化する。
    /// modulate は子ノードへも乗算で伝播するため、カード全体（氏名・ジョブ・HP）が
    /// まとめて褪色する。対象パネルが次の再描画で QueueFree される際、Tween も自動失効する。
    /// </summary>
    public static Tween? FadeToDeath(Control? node, Color deathTint, double seconds)
    {
        if (node is null || !GodotObject.IsInstanceValid(node))
        {
            return null;
        }

        var tween = node.CreateTween();
        tween.TweenProperty(node, "modulate", deathTint, seconds)
             .SetTrans(Tween.TransitionType.Sine)
             .SetEase(Tween.EaseType.In);
        return tween;
    }

    // ─── HP ドレイン（Range の value を遅延付きで減衰させる。被害の重みの可視化） ──

    /// <summary>
    /// バー（Range / ProgressBar）の value を、短い <paramref name="delaySeconds"/> の
    /// 溜めの後に <paramref name="toValue"/> まで滑らかに減衰させる。前面バーが即座に
    /// 新 HP を示し、その背後でこのドレインバーが「遅れて追従」することで、削れた量の
    /// 重みを尾を引くように見せる（格闘ゲーム風のディレイ HP バー）。
    /// </summary>
    public static Tween? DrainBar(Range? bar, double toValue, double delaySeconds, double durationSeconds)
    {
        if (bar is null || !GodotObject.IsInstanceValid(bar))
        {
            return null;
        }

        var tween = bar.CreateTween();
        tween.TweenProperty(bar, "value", toValue, durationSeconds)
             .SetDelay(delaySeconds)
             .SetTrans(Tween.TransitionType.Cubic)
             .SetEase(Tween.EaseType.Out);
        return tween;
    }

    // ─── 数値ロールアップ（Label のテキストを from→to へ整数カウントアップ） ──────

    /// <summary>
    /// <see cref="Label"/> のテキストを <paramref name="fromValue"/> から
    /// <paramref name="toValue"/> へ整数で滑らかにカウントアップさせ、獲得点数が
    /// 勢いよく積み上がる「高揚感」を物質化する。各フレームの数値は
    /// <paramref name="format"/>（呼び出し側が所有する整形デリゲート）でテキスト化される
    /// ため、本クラス自身は表示文言を一切内包しない（設計憲法 ①）。
    ///
    /// ★ リーク・ゾンビ化の構造的根絶: Tween は対象 Label 自身へバインドされる。Label が
    ///   QueueFree（更地描画 / _ExitTree）されると Tween は Godot 側で自動失効し、
    ///   TweenMethod のコールバックは <see cref="GodotObject.IsInstanceValid"/> ガードに
    ///   より安全に no-op となる（null 参照・二重発火なし）。
    /// ★ 補間は float で進み、各ステップで最近傍整数へ丸めて表示する。終端では toValue に
    ///   一致する（整数域の小さな値では丸め誤差は発生しない）。
    /// </summary>
    public static Tween? CountUp(
        Label? label,
        int fromValue,
        int toValue,
        System.Func<int, string>? format,
        double seconds,
        double delaySeconds = 0.0)
    {
        if (label is null || !GodotObject.IsInstanceValid(label) || format is null)
        {
            return null;
        }

        // null 確定済みの参照を非 nullable のローカルへ束ね、クロージャ内のフロー解析を安全化する。
        Label target = label;
        System.Func<int, string> render = format;

        // 開始時は始点の整形済みテキストを即時反映（補間開始前のチラつきを防ぐ）。
        target.Text = render(fromValue);

        var tween = target.CreateTween();
        tween.TweenMethod(
                 Callable.From<float>(current =>
                 {
                     if (GodotObject.IsInstanceValid(target))
                     {
                         target.Text = render((int)System.MathF.Round(current));
                     }
                 }),
                 (float)fromValue,
                 (float)toValue,
                 seconds)
             .SetDelay(delaySeconds)
             .SetTrans(Tween.TransitionType.Cubic)
             .SetEase(Tween.EaseType.Out);
        return tween;
    }

    // ─── 線の伸長（Line2D の終点を始点から目標へ補間。親→子コネクタの開通演出） ──

    /// <summary>
    /// <see cref="Line2D"/> の 2 点（始点・終点）を、終点だけ <paramref name="from"/> から
    /// <paramref name="to"/> まで補間させ「線が親から子へスーッと伸びていく」開通演出を焚く。
    /// 家系図オーバーレイで、各コネクタを世代帯の上から下へ順に開通させるのに使う。
    ///
    /// ★ 座標系: Line2D は Node2D。Control の子として追加された場合、その Points は
    ///   親 Control のローカル座標フレームを共有する。呼び出し側は from/to を当の
    ///   キャンバス Control のローカル座標で渡すこと（ノード絶対配置と同じフレーム）。
    /// ★ リーク・ゾンビ化の根絶: Tween は対象 Line2D 自身へバインドされる。コネクタが
    ///   QueueFree（オーバーレイ更地化）されると Tween は自動失効し、TweenMethod の
    ///   コールバックは IsInstanceValid ガードにより安全に no-op となる（null 参照なし）。
    /// ★ <paramref name="delaySeconds"/> で世代帯ごとの開通に時間差を付けられる。
    /// </summary>
    public static Tween? GrowLine(Line2D? line, Vector2 from, Vector2 to, double seconds, double delaySeconds = 0.0)
    {
        if (line is null || !GodotObject.IsInstanceValid(line))
        {
            return null;
        }

        // null 確定済みの参照を非 nullable のローカルへ束ねる。これにより、後続のクロージャ
        // 内での Points 代入が nullable フロー解析でも安全（CS8602 を構造的に回避）になる。
        Line2D target = line;

        // 開始時は長さ 0（始点に終点が重なった点）から伸ばし始める。
        target.Points = new[] { from, from };

        var tween = target.CreateTween();
        tween.TweenMethod(
                 Callable.From<Vector2>(tip =>
                 {
                     if (GodotObject.IsInstanceValid(target))
                     {
                         target.Points = new[] { from, tip };
                     }
                 }),
                 from,
                 to,
                 seconds)
             .SetDelay(delaySeconds)
             .SetTrans(Tween.TransitionType.Cubic)
             .SetEase(Tween.EaseType.Out);
        return tween;
    }

    // ─── 自己崩壊型ポップアップ（生成 → 上昇フェード → 自分自身を QueueFree） ──────

    /// <summary>
    /// オーバーレイ層へ軽量 Label を生成し、アンカー上空へ跳ね上げつつアルファを 0 へ
    /// フェードさせ、Tween 完了時に自分自身を QueueFree する自己崩壊型ポップアップ。
    ///
    /// ★ リーク・ゾンビ化の構造的根絶: Tween は Label 自身へバインドされ、最終ステップの
    ///   TweenCallback が当の Label を QueueFree する。万一、親（オーバーレイ層）の
    ///   cascade 解放が先に走っても、Label 解放で Tween は自動失効し、コールバックは
    ///   IsInstanceValid ガードにより安全に no-op となる（二重解放なし）。
    /// ★ ① 準拠: 表示テキスト・testid は呼び出し側が組み立てて渡す（本クラスは生成しない）。
    /// </summary>
    public static void RisingPopup(
        Control? overlayLayer,
        Control? anchor,
        string text,
        Color color,
        string testIdMetaKey,
        string testId,
        float riseDistance,
        double lifeSeconds)
    {
        if (overlayLayer is null || !GodotObject.IsInstanceValid(overlayLayer))
        {
            return;
        }
        if (anchor is null || !GodotObject.IsInstanceValid(anchor))
        {
            return;
        }

        var label = new Label
        {
            Text        = text,
            Modulate    = color,
            ZIndex      = 100,                              // オーバーレイ層内でも確実に最前面
            MouseFilter = Control.MouseFilterEnum.Ignore,   // クリックを透過（下のボタンを妨げない）
        };
        label.SetMeta(testIdMetaKey, testId);
        overlayLayer.AddChild(label);

        // アンカーのグローバル矩形から、その上部中央を初期位置に取る。
        var anchorRect = anchor.GetGlobalRect();
        label.GlobalPosition = new Vector2(
            anchorRect.Position.X + anchorRect.Size.X * 0.5f,
            anchorRect.Position.Y + anchorRect.Size.Y * 0.25f);

        var risenLocalPosition = label.Position + new Vector2(0.0f, -riseDistance);

        var tween = label.CreateTween();
        // 上昇とフェードを並行（Parallel）で走らせる。
        tween.TweenProperty(label, "position", risenLocalPosition, lifeSeconds)
             .SetTrans(Tween.TransitionType.Quad)
             .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(label, "modulate:a", 0.0f, lifeSeconds)
             .SetTrans(Tween.TransitionType.Sine)
             .SetEase(Tween.EaseType.In);
        // 並行ブロックの後（Chain）に、自分自身を QueueFree する自己崩壊ステップを連結。
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(label))
            {
                label.QueueFree();
            }
        }));
    }
}
