// using は Rust の use に相当。名前空間を省略して型名を書けるようにする
using System.Linq;
using System.Text;
using UnityEditor;  // Editor 拡張用の API（MenuItem など）
using UnityEngine;  // Debug.Log など Unity の基本 API

namespace SakuraWine.MAShapeSyncAuto
{
    // static class = インスタンスを作らない関数置き場（Rust のモジュール内 fn の集まりに近い）
    // internal = このアセンブリ（asmdef）の中からだけ見える。Rust の pub(crate) 相当
    internal static class MenuEntry
    {
        private const string MenuPath = "Tools/SakuraWine/MA Shape Sync Auto";
        private const string LogPrefix = "[MA Shape Sync Auto]";

        // 第 2 引数 true の MenuItem は「検証用関数」。false を返すとメニューがグレーアウトする
        [MenuItem(MenuPath, true)]
        private static bool Validate() => Selection.activeGameObject != null; // => は 1 行で書く関数（Rust の式本体に近い）

        // [MenuItem] 属性を付けた static メソッドが、Unity のメニューに登録される
        // （Rust の #[...] 属性 / Python のデコレータのようなもの）
        [MenuItem(MenuPath)]
        private static void Run()
        {
            // Hierarchy で選択中のオブジェクト＝衣装ルート
            var outfit = Selection.activeGameObject;

            var avatarRoot = BlendshapeSyncBinder.FindAvatarRoot(outfit);
            if (avatarRoot == null)
            {
                // 第 2 引数にオブジェクトを渡すと、Console のログをクリックしたときにそのオブジェクトがハイライトされる
                Debug.LogWarning($"{LogPrefix} 「{outfit.name}」の親に VRC Avatar Descriptor が見つかりません。衣装をアバターの中に入れてから実行してください。", outfit);
                return;
            }
            if (avatarRoot == outfit)
            {
                Debug.LogWarning($"{LogPrefix} アバタールートではなく、衣装のルートオブジェクトを選択して実行してください。", outfit);
                return;
            }

            // ボディ候補を表示してから、いちばんスコアの高いものを使う
            var candidates = BlendshapeSyncBinder.RankBodyCandidates(avatarRoot, outfit);
            var body = candidates.FirstOrDefault(c => c.Score > 0)?.Renderer;

            // StringBuilder は文字列を効率よく組み立てるクラス（Rust の String に push_str していくイメージ）
            var candidateLog = new StringBuilder($"{LogPrefix} ボディ候補（共通シェイプキー数の合計）:\n");
            foreach (var c in candidates)
                candidateLog.AppendLine($"  {(c.Renderer == body ? "★" : "  ")} {c.Renderer.name}: {c.Score}");
            Debug.Log(candidateLog.ToString(), avatarRoot);

            if (body == null)
            {
                Debug.LogWarning($"{LogPrefix} 衣装と共通のシェイプキーを持つボディが見つかりませんでした。", outfit);
                return;
            }

            var result = BlendshapeSyncBinder.Bind(body, outfit);
            LogResult(outfit, result);
        }

        private static void LogResult(GameObject outfit, BindResult result)
        {
            var log = new StringBuilder($"{LogPrefix} 「{outfit.name}」に設定しました（ボディ: {result.Body.name}）\n");
            foreach (var m in result.Meshes)
            {
                // switch 式：Rust の match に近い
                var detail = m.Status switch
                {
                    MeshBindStatus.Added => $"{m.AddedCount} 個追加（共通 {m.CommonCount} 個）" + (m.ComponentCreated ? " ※コンポーネント新規追加" : ""),
                    MeshBindStatus.UpToDate => $"登録済み（共通 {m.CommonCount} 個）",
                    MeshBindStatus.NoCommonShapes => "共通シェイプキーなし → スキップ",
                    MeshBindStatus.NoMesh => "メッシュ未設定 → スキップ",
                    _ => m.Status.ToString(), // _ は Rust と同じ「その他すべて」
                };
                log.AppendLine($"  {m.Renderer.name}: {detail}");
            }
            Debug.Log(log.ToString(), outfit);
        }
    }
}
