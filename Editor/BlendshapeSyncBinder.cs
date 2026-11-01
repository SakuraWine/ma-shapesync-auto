using System;
using System.Collections.Generic;
using System.Linq; // LINQ：Where / Select など。Rust のイテレータアダプタ（filter / map）に相当
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace SakuraWine.MAShapeSyncAuto
{
    // ロジック本体。UI（メニュー・ウィンドウ）には依存しない。Rust で言う lib.rs の役割
    internal static class BlendshapeSyncBinder
    {
        private const string UndoName = "MA Shape Sync Auto";

        // 親をたどって VRC Avatar Descriptor を持つオブジェクト（アバタールート）を探す。見つからなければ null
        // C# には Option<T> が無いので、「無い」は null で表す（呼び出し側で null チェックが必要）
        public static GameObject FindAvatarRoot(GameObject obj)
        {
            // GetComponentInParent は自分自身 → 親 → … の順に探す。true は「非アクティブも含める」
            var descriptor = obj.GetComponentInParent<VRCAvatarDescriptor>(true);
            return descriptor != null ? descriptor.gameObject : null;
        }

        // メッシュのシェイプキー名を、メッシュ内の並び順どおりに列挙する
        // IEnumerable<T> + yield return は Rust の impl Iterator<Item = T> を返す関数に近い（遅延評価）
        public static IEnumerable<string> GetBlendshapeNames(Mesh mesh)
        {
            for (var i = 0; i < mesh.blendShapeCount; i++)
                yield return mesh.GetBlendShapeName(i);
        }

        // ボディと衣装で名前が一致するシェイプキーを、衣装側の並び順で返す
        public static List<string> FindCommonBlendshapes(Mesh body, Mesh outfit)
        {
            // HashSet = Rust の HashSet。Contains が O(1) なので、毎回リストを線形探索するより速い
            var bodyNames = new HashSet<string>(GetBlendshapeNames(body));
            return GetBlendshapeNames(outfit).Where(bodyNames.Contains).ToList();
        }

        // 衣装配下の SkinnedMeshRenderer をすべて列挙（アウター・スカートなど子オブジェクトも含む）
        public static List<SkinnedMeshRenderer> GetOutfitRenderers(GameObject outfitRoot)
        {
            // true = 非アクティブな子も含める（衣装の一部が OFF になっていても対象にする）
            return outfitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToList();
        }

        // ボディ候補をスコア順（高い順）に並べて返す
        // 候補 = アバター配下の SkinnedMeshRenderer のうち、衣装の外にあり、シェイプキーを持つもの
        // スコア = 衣装の全メッシュについて「共通シェイプキーの数」を合計したもの
        public static List<BodyCandidate> RankBodyCandidates(GameObject avatarRoot, GameObject outfitRoot)
        {
            var outfitMeshes = GetOutfitRenderers(outfitRoot)
                .Where(r => r.sharedMesh != null)
                .Select(r => r.sharedMesh)
                .ToList();

            return avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                // IsChildOf は「自分自身」も true になるので、衣装ルート自体も除外される
                .Where(r => !r.transform.IsChildOf(outfitRoot.transform))
                .Where(r => r.sharedMesh != null && r.sharedMesh.blendShapeCount > 0)
                .Select(r => new BodyCandidate
                {
                    Renderer = r,
                    Score = outfitMeshes.Sum(m => FindCommonBlendshapes(r.sharedMesh, m).Count),
                })
                .OrderByDescending(c => c.Score)
                .ToList();
        }

        // 最もスコアが高い候補をボディとする。共通シェイプキーが 1 つも無ければ null
        public static SkinnedMeshRenderer DetectBody(GameObject avatarRoot, GameObject outfitRoot)
        {
            // FirstOrDefault は「最初の要素、無ければ null」。Rust の .next() が None を返すイメージ
            // ?. は null なら null を返す演算子（Rust の Option::map に近い）
            return RankBodyCandidates(avatarRoot, outfitRoot)
                .FirstOrDefault(c => c.Score > 0)?.Renderer;
        }

        // 衣装配下の各メッシュに MA Blendshape Sync を設定する。全体を 1 回の Undo にまとめる
        public static BindResult Bind(SkinnedMeshRenderer body, GameObject outfitRoot)
        {
            // 引数チェック。Rust なら型で防げるところを、C# では実行時に例外を投げて防ぐ
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (outfitRoot == null) throw new ArgumentNullException(nameof(outfitRoot));
            if (body.sharedMesh == null) throw new ArgumentException("Body has no mesh.", nameof(body));

            // Undo グループ：この間に記録した操作が Ctrl+Z 1 回でまとめて戻るようにする
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);

            var result = new BindResult { Body = body };

            foreach (var renderer in GetOutfitRenderers(outfitRoot))
            {
                result.Meshes.Add(BindMesh(body, renderer));
            }

            Undo.CollapseUndoOperations(undoGroup);
            return result;
        }

        // 衣装メッシュ 1 つ分の処理
        private static MeshBindResult BindMesh(SkinnedMeshRenderer body, SkinnedMeshRenderer renderer)
        {
            var meshResult = new MeshBindResult { Renderer = renderer };

            if (renderer.sharedMesh == null)
            {
                meshResult.Status = MeshBindStatus.NoMesh;
                return meshResult;
            }

            var common = FindCommonBlendshapes(body.sharedMesh, renderer.sharedMesh);
            meshResult.CommonCount = common.Count;

            // 共通シェイプキーが 0 個ならコンポーネントを付けない（既存のものにも触らない）
            if (common.Count == 0)
            {
                meshResult.Status = MeshBindStatus.NoCommonShapes;
                return meshResult;
            }

            // 既にあれば再利用、無ければ Undo 付きで追加（Undo.AddComponent なら Ctrl+Z で削除される）
            var sync = renderer.GetComponent<ModularAvatarBlendshapeSync>();
            if (sync == null)
            {
                sync = Undo.AddComponent<ModularAvatarBlendshapeSync>(renderer.gameObject);
                meshResult.ComponentCreated = true;
            }

            // これから sync を書き換えることを Undo に記録（変更「前」に呼ぶのがルール）
            Undo.RecordObject(sync, UndoName);

            // 登録済みのシェイプキー名を集める
            // MA の Bindings.Contains() は「参照先オブジェクトの持ち方」まで比較するので、
            // 参照パスだけで登録された既存 Binding（Prim など）と別物扱いになり二重登録してしまう。
            // そこで「実際にボディを指しているか」で判定する
            var registered = new HashSet<string>(
                sync.Bindings
                    .Where(b => b.ReferenceMesh != null && b.ReferenceMesh.Get(sync) == body.gameObject)
                    .Select(b => b.Blendshape));

            foreach (var shapeName in common)
            {
                // HashSet.Add は「新しく追加できたら true」。Rust の HashSet::insert と同じ
                if (!registered.Add(shapeName)) continue;

                // BlendshapeBinding は struct（値型）なので、ここで作った値がリストにコピーされる
                sync.Bindings.Add(new BlendshapeBinding
                {
                    ReferenceMesh = new AvatarObjectReference(body.gameObject), // アバタールートからのパスも自動で設定される
                    Blendshape = shapeName,       // ボディ側のシェイプキー名
                    LocalBlendshape = shapeName,  // 衣装側のシェイプキー名（今は同名のみ。将来の名前ゆれ対応でここが変わる）
                    // RemapCurve は未設定のままで OK（MA が 0→0, 100→100 で初期化する）
                });
                meshResult.AddedCount++;
            }

            meshResult.Status = meshResult.AddedCount > 0 ? MeshBindStatus.Added : MeshBindStatus.UpToDate;

            // 保存対象にする。シーン上の Prefab インスタンスの場合は、オーバーライドとして記録しないと
            // 変更が失われることがあるので、MA 本体（RuntimeUtil.MarkDirty）と同じ処理をする
            if (PrefabUtility.IsPartOfPrefabInstance(sync))
                PrefabUtility.RecordPrefabInstancePropertyModifications(sync);
            EditorUtility.SetDirty(sync);

            return meshResult;
        }
    }
}
