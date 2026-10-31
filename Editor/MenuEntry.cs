// using は Rust の use に相当。名前空間を省略して型名を書けるようにする
using nadena.dev.modular_avatar.core; // MA のランタイム側（ModularAvatarBlendshapeSync など）
using UnityEditor;                     // Editor 拡張用の API（MenuItem など）
using UnityEngine;                     // Debug.Log など Unity の基本 API
using VRC.SDK3.Avatars.Components;     // VRCAvatarDescriptor

namespace SakuraWine.MAShapeSyncAuto
{
    // static class = インスタンスを作らない関数置き場（Rust のモジュール内 fn の集まりに近い）
    // internal = このアセンブリ（asmdef）の中からだけ見える。Rust の pub(crate) 相当
    internal static class MenuEntry
    {
        // [MenuItem] 属性を付けた static メソッドが、Unity のメニューに登録される
        // （Rust の #[...] 属性 / Python のデコレータのようなもの）
        [MenuItem("Tools/SakuraWine/MA Shape Sync Auto")]
        private static void Run()
        {
            // Step 2 の動作確認用：asmdef の参照が正しければ、MA と VRC SDK の型名が表示される
            // typeof(T) は型の情報を取り出す演算子（Rust の std::any::type_name::<T>() に近い）
            Debug.Log("[MA Shape Sync Auto] Hello! " +
                      $"MA: {typeof(ModularAvatarBlendshapeSync).FullName}, " +
                      $"VRC: {typeof(VRCAvatarDescriptor).FullName}");
        }
    }
}
