# 変更履歴

このパッケージの主な変更点をここに記録します。
書式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に、
バージョン番号は [セマンティック バージョニング](https://semver.org/lang/ja/) に従います。

## [Unreleased]

### 変更

- README にインストール方法（VPM リポジトリの追加手順）を追加
- package.json に `documentationUrl` を追加
- `LICENSE` を `LICENSE.md` に改名（GitHub で `.meta` がライセンスとして表示される問題への対策）

## [0.1.0] - 2026-11-01

### 追加

- `Tools/SakuraWine/MA Shape Sync Auto` ウィンドウ（UI Toolkit）：衣装を指定し、
  自動検出されたボディの確認・変更と、メッシュごとのプレビューをしてから実行できる
- 衣装と共通するシェイプキーの数からボディのメッシュを自動検出
- ボディと共通のシェイプキーを持つ衣装メッシュすべてに MA Blendshape Sync を設定
  （既存のコンポーネントは再利用、登録済みのシェイプキーはスキップ、Ctrl+Z 1 回で元に戻せる）
- エディタテスト（Unity Test Framework）
- VPM の依存関係として Modular Avatar（1.18.0 以上 2.0.0 未満）を宣言
