using System.Collections.Generic;
using UnityEngine;

namespace SakuraWine.MAShapeSyncAuto
{
    // enum は Rust の enum（データを持たない版）とほぼ同じ
    internal enum MeshBindStatus
    {
        Added,          // 新しく Binding を追加した
        UpToDate,       // 共通シェイプキーはあるが、すべて登録済みだった
        NoCommonShapes, // ボディと共通のシェイプキーが無い（コンポーネントは付けない）
        NoMesh,         // SkinnedMeshRenderer にメッシュが設定されていない
    }

    // ボディ候補と、そのスコア（衣装全体と共通するシェイプキーの数）
    // class は参照型（Rust で言うと Rc<RefCell<T>> 的に共有される）。struct は値型（Copy な struct）
    internal sealed class BodyCandidate
    {
        public SkinnedMeshRenderer Renderer;
        public int Score;
    }

    // 衣装メッシュ 1 つ分の結果
    internal sealed class MeshBindResult
    {
        public SkinnedMeshRenderer Renderer;
        public MeshBindStatus Status;
        public int CommonCount;        // ボディと共通するシェイプキーの数
        public int AddedCount;         // 今回新しく追加した Binding の数
        public bool ComponentCreated;  // MA Blendshape Sync を新しく付けたか（false なら既存を再利用 or 付けていない）
    }

    // 1 回の実行全体の結果
    internal sealed class BindResult
    {
        public SkinnedMeshRenderer Body;
        // new() は C# 9 の「型推論つき new」。Rust の Vec::new() に相当
        public List<MeshBindResult> Meshes = new();
    }
}
