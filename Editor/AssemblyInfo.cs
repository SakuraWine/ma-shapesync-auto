using System.Runtime.CompilerServices;

// internal なクラスを、テスト用アセンブリからも見えるようにする設定
// Rust では #[cfg(test)] mod tests が同じ crate 内なので private も見えるが、
// C# ではテストが別アセンブリになるので、こうして明示的に許可する
[assembly: InternalsVisibleTo("SakuraWine.MAShapeSyncAuto.Editor.Tests")]
