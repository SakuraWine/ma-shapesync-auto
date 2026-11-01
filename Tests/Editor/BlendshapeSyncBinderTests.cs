using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using NUnit.Framework; // NUnit：C# の定番テストフレームワーク。Python の pytest / unittest に相当
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace SakuraWine.MAShapeSyncAuto.Tests
{
    // テスト用に組み立てるアバター（購入アセットは使わず、コードで最小構成を作る）
    //
    // Avatar (VRCAvatarDescriptor)
    // ├─ Body      … シェイプキー: Face_Smile           （名前は Body だが本当のボディではない）
    // ├─ Body2     … シェイプキー: Belly_Slim, Breast_Big, Leg_Thin  （本当のボディ）
    // └─ Outfit
    //    ├─ Outer  … シェイプキー: Belly_Slim, Breast_Big, Outer_Only
    //    ├─ Skirt  … シェイプキー: Leg_Thin
    //    └─ Ribbon … シェイプキー: Ribbon_Long          （ボディと共通なし）
    //
    // [TestFixture] = テストクラス。Rust の #[cfg(test)] mod tests に相当
    [TestFixture]
    internal class BlendshapeSyncBinderTests
    {
        private GameObject _avatar;
        private SkinnedMeshRenderer _fakeBody;
        private SkinnedMeshRenderer _body;
        private GameObject _outfit;
        private SkinnedMeshRenderer _outer;
        private SkinnedMeshRenderer _skirt;
        private SkinnedMeshRenderer _ribbon;
        private readonly List<Mesh> _meshes = new();

        // [SetUp] は各テストの前に毎回呼ばれる（pytest の fixture に近い）
        [SetUp]
        public void SetUp()
        {
            _avatar = new GameObject("Avatar");
            _avatar.AddComponent<VRCAvatarDescriptor>();

            _fakeBody = CreateMeshObject("Body", _avatar, "Face_Smile");
            _body = CreateMeshObject("Body2", _avatar, "Belly_Slim", "Breast_Big", "Leg_Thin");

            _outfit = new GameObject("Outfit");
            _outfit.transform.SetParent(_avatar.transform);
            _outer = CreateMeshObject("Outer", _outfit, "Belly_Slim", "Breast_Big", "Outer_Only");
            _skirt = CreateMeshObject("Skirt", _outfit, "Leg_Thin");
            _ribbon = CreateMeshObject("Ribbon", _outfit, "Ribbon_Long");
        }

        // [TearDown] は各テストの後に毎回呼ばれる。作ったオブジェクトを片付ける（Rust の Drop のように自動では消えない）
        [TearDown]
        public void TearDown()
        {
            // テスト中の Bind() が Undo 履歴に記録を残すので、先にそれを消しておく。
            // 消さないと、Test Runner が終了時に Undo を巻き戻すとき、削除済みのオブジェクトが
            // 「親なし」で復元されて Hierarchy に残ってしまう
            foreach (var component in _avatar.GetComponentsInChildren<Component>(true))
            {
                Undo.ClearUndo(component);
                Undo.ClearUndo(component.gameObject);
            }

            Object.DestroyImmediate(_avatar);
            foreach (var mesh in _meshes) Object.DestroyImmediate(mesh);
            _meshes.Clear();
        }

        // 指定したシェイプキーを持つ、三角形 1 枚だけのメッシュを作って GameObject に付ける
        private SkinnedMeshRenderer CreateMeshObject(string name, GameObject parent, params string[] shapeNames)
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            var deltas = new Vector3[3]; // 変形量はゼロでよい（名前さえあればテストには十分）
            foreach (var shape in shapeNames)
                mesh.AddBlendShapeFrame(shape, 100f, deltas, null, null);
            _meshes.Add(mesh);

            var go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            return smr;
        }

        // [Test] を付けたメソッドが 1 つのテストケース（Rust の #[test]）
        [Test]
        public void FindCommonBlendshapes_ReturnsSharedNamesInOutfitOrder()
        {
            var common = BlendshapeSyncBinder.FindCommonBlendshapes(_body.sharedMesh, _outer.sharedMesh);

            // Assert.That(実際の値, 条件) の形で書く。Rust の assert_eq! に相当
            Assert.That(common, Is.EqualTo(new[] { "Belly_Slim", "Breast_Big" }));
        }

        [Test]
        public void FindAvatarRoot_FindsDescriptorInParents()
        {
            Assert.That(BlendshapeSyncBinder.FindAvatarRoot(_outer.gameObject), Is.EqualTo(_avatar));
        }

        [Test]
        public void GetOutfitRenderers_IncludesAllChildParts()
        {
            var renderers = BlendshapeSyncBinder.GetOutfitRenderers(_outfit);

            Assert.That(renderers, Is.EquivalentTo(new[] { _outer, _skirt, _ribbon })); // 順不同で比較
        }

        [Test]
        public void DetectBody_PicksMeshWithMostCommonShapes_NotByName()
        {
            // 「Body」という名前のメッシュではなく、共通シェイプキーの多い Body2 が選ばれること（Ramune と同じ状況）
            Assert.That(BlendshapeSyncBinder.DetectBody(_avatar, _outfit), Is.EqualTo(_body));
        }

        [Test]
        public void RankBodyCandidates_ExcludesOutfitMeshes()
        {
            var candidates = BlendshapeSyncBinder.RankBodyCandidates(_avatar, _outfit).Select(c => c.Renderer);

            Assert.That(candidates, Is.EquivalentTo(new[] { _body, _fakeBody }));
        }

        [Test]
        public void Bind_AddsComponentOnlyToMeshesWithCommonShapes()
        {
            var result = BlendshapeSyncBinder.Bind(_body, _outfit);

            Assert.That(_outer.GetComponent<ModularAvatarBlendshapeSync>(), Is.Not.Null);
            Assert.That(_skirt.GetComponent<ModularAvatarBlendshapeSync>(), Is.Not.Null);
            Assert.That(_ribbon.GetComponent<ModularAvatarBlendshapeSync>(), Is.Null);

            var outerResult = result.Meshes.Single(m => m.Renderer == _outer);
            Assert.That(outerResult.Status, Is.EqualTo(MeshBindStatus.Added));
            Assert.That(outerResult.AddedCount, Is.EqualTo(2));
            Assert.That(outerResult.ComponentCreated, Is.True);

            var ribbonResult = result.Meshes.Single(m => m.Renderer == _ribbon);
            Assert.That(ribbonResult.Status, Is.EqualTo(MeshBindStatus.NoCommonShapes));
        }

        [Test]
        public void Bind_CreatesBindingsThatReferenceBody()
        {
            BlendshapeSyncBinder.Bind(_body, _outfit);

            var sync = _outer.GetComponent<ModularAvatarBlendshapeSync>();
            Assert.That(sync.Bindings.Select(b => b.Blendshape), Is.EqualTo(new[] { "Belly_Slim", "Breast_Big" }));
            foreach (var binding in sync.Bindings)
            {
                Assert.That(binding.ReferenceMesh.Get(sync), Is.EqualTo(_body.gameObject));
                Assert.That(binding.ReferenceMesh.referencePath, Is.EqualTo("Body2"));
                Assert.That(binding.LocalBlendshape, Is.EqualTo(binding.Blendshape));
            }
        }

        [Test]
        public void Bind_RunTwice_DoesNotDuplicate()
        {
            BlendshapeSyncBinder.Bind(_body, _outfit);
            var second = BlendshapeSyncBinder.Bind(_body, _outfit);

            Assert.That(_outer.GetComponent<ModularAvatarBlendshapeSync>().Bindings.Count, Is.EqualTo(2));
            Assert.That(second.Meshes.Single(m => m.Renderer == _outer).Status, Is.EqualTo(MeshBindStatus.UpToDate));
            Assert.That(_outer.GetComponents<ModularAvatarBlendshapeSync>().Length, Is.EqualTo(1)); // コンポーネントも 1 個のまま
        }

        [Test]
        public void Bind_ReusesExistingComponent_WithPathOnlyBinding()
        {
            // Prim のように「パスだけ」で登録済みの Binding がある状態を再現
            var existing = _outer.gameObject.AddComponent<ModularAvatarBlendshapeSync>();
            existing.Bindings.Add(new BlendshapeBinding
            {
                ReferenceMesh = new AvatarObjectReference { referencePath = "Body2" },
                Blendshape = "Belly_Slim",
                LocalBlendshape = "Belly_Slim",
            });

            var result = BlendshapeSyncBinder.Bind(_body, _outfit);

            var outerResult = result.Meshes.Single(m => m.Renderer == _outer);
            Assert.That(outerResult.ComponentCreated, Is.False);
            Assert.That(outerResult.AddedCount, Is.EqualTo(1)); // Breast_Big だけ追加
            Assert.That(existing.Bindings.Select(b => b.Blendshape), Is.EqualTo(new[] { "Belly_Slim", "Breast_Big" }));
        }

        [Test]
        public void Preview_DoesNotModifyAnything()
        {
            BlendshapeSyncBinder.Preview(_body, _outfit);

            Assert.That(_outer.GetComponent<ModularAvatarBlendshapeSync>(), Is.Null);
            Assert.That(_skirt.GetComponent<ModularAvatarBlendshapeSync>(), Is.Null);
        }

        [Test]
        public void Preview_MatchesBindResult()
        {
            var preview = BlendshapeSyncBinder.Preview(_body, _outfit);
            var result = BlendshapeSyncBinder.Bind(_body, _outfit);

            // プレビューで見せた内容と、実際に実行した結果が一致すること
            Assert.That(preview.Meshes.Select(m => (m.Renderer, m.Status, m.AddedCount, m.ComponentCreated)),
                Is.EqualTo(result.Meshes.Select(m => (m.Renderer, m.Status, m.AddedCount, m.ComponentCreated))));
        }

        [Test]
        public void Bind_CanBeUndoneInOneStep()
        {
            BlendshapeSyncBinder.Bind(_body, _outfit);
            Undo.PerformUndo(); // Ctrl+Z 1 回分

            Assert.That(_outer.GetComponent<ModularAvatarBlendshapeSync>(), Is.Null);
            Assert.That(_skirt.GetComponent<ModularAvatarBlendshapeSync>(), Is.Null);
        }
    }
}
