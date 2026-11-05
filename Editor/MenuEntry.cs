// using は Rust の use に相当。名前空間を省略して型名を書けるようにする
using UnityEditor; // Editor 拡張用の API（MenuItem など）

namespace SakuraWine.MAShapeSyncAuto
{
    // static class = インスタンスを作らない関数置き場（Rust のモジュール内 fn の集まりに近い）
    // internal = このアセンブリ（asmdef）の中からだけ見える。Rust の pub(crate) 相当
    // ここはエントリーポイント（Rust の main.rs）。実際の処理はウィンドウとロジック側に任せる
    internal static class MenuEntry
    {
        // [MenuItem] 属性を付けた static メソッドが、Unity のメニューに登録される
        // （Rust の #[...] 属性 / Python のデコレータのようなもの）
        [MenuItem("Tools/Sakura-iro Wine Cellar/MA Shape Sync Auto")]
        private static void Open() => ShapeSyncWindow.Open();
    }
}
