using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements; // ObjectField などエディタ専用の UI 要素
using UnityEngine;
using UnityEngine.UIElements; // VisualElement / Label / Button など UI Toolkit の基本要素

namespace SakuraWine.MAShapeSyncAuto
{
    // EditorWindow を継承したクラスが 1 つのウィンドウになる
    // sealed = これ以上継承させない（Rust の struct は継承が無いので、そのイメージに近い）
    internal sealed class ShapeSyncWindow : EditorWindow
    {
        // パッケージ内のファイルは "Packages/<パッケージ名>/..." というパスで読み込める
        private const string UiDir = "Packages/io.github.sakurawine.ma-shapesync-auto/Editor/UI/";
        private const string LogPrefix = "[MA Shape Sync Auto]";

        // ---- 状態 ----
        // [SerializeField] を付けたフィールドは、スクリプトの再コンパイル（ドメインリロード）後も値が残る
        [SerializeField] private GameObject _outfit;
        [SerializeField] private SkinnedMeshRenderer _body;
        private GameObject _avatar;

        // ---- UI 要素への参照（CreateGUI で UXML から探して保持する）----
        private ObjectField _outfitField;
        private ObjectField _avatarField;
        private VisualElement _bodyContainer;
        private VisualElement _messageContainer;
        private Label _listHeader;
        private ScrollView _meshList;
        private Button _runButton;

        // メニューから呼ばれる。ウィンドウを開き（既に開いていれば前面に出し）、選択中のオブジェクトを衣装にセットする
        public static void Open()
        {
            var window = GetWindow<ShapeSyncWindow>();
            window.titleContent = new GUIContent("MA Shape Sync Auto");
            window.minSize = new Vector2(360, 300);

            if (Selection.activeGameObject != null)
                window.SetOutfit(Selection.activeGameObject);
        }

        // ---- ライフサイクル ----
        // Unity が決まったタイミングで呼ぶメソッド群（Rust のトレイト実装を Unity 側が呼ぶイメージ）

        private void OnEnable()
        {
            // Ctrl+Z / Ctrl+Y されたらプレビューを作り直す（イベント購読。+= で登録、-= で解除）
            Undo.undoRedoPerformed += Refresh;
        }

        private void OnDisable()
        {
            // 解除を忘れると、閉じたウィンドウが呼ばれ続けてしまう（購読のリーク）
            Undo.undoRedoPerformed -= Refresh;
        }

        // UI Toolkit のウィンドウは、ここで UI を組み立てる
        private void CreateGUI()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UiDir + "ShapeSyncWindow.uxml");
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(UiDir + "ShapeSyncWindow.uss");
            if (tree == null)
            {
                rootVisualElement.Add(new HelpBox($"UXML が見つかりません: {UiDir}", HelpBoxMessageType.Error));
                return;
            }

            // UXML の中身を rootVisualElement の下に複製する（HTML をページに流し込むイメージ）
            tree.CloneTree(rootVisualElement);
            if (style != null) rootVisualElement.styleSheets.Add(style);

            // Q<T>("name") は name で要素を探す（JS の querySelector('#name') に相当）
            _outfitField = rootVisualElement.Q<ObjectField>("outfit-field");
            _avatarField = rootVisualElement.Q<ObjectField>("avatar-field");
            _bodyContainer = rootVisualElement.Q<VisualElement>("body-container");
            _messageContainer = rootVisualElement.Q<VisualElement>("message-container");
            _listHeader = rootVisualElement.Q<Label>("list-header");
            _meshList = rootVisualElement.Q<ScrollView>("mesh-list");
            _runButton = rootVisualElement.Q<Button>("run-button");

            // ObjectField が受け付ける型は UXML では指定しにくいので C# 側で設定
            _outfitField.objectType = typeof(GameObject);
            _outfitField.allowSceneObjects = true;
            // 値が変わったときのコールバック（ラムダ式 evt => {...} は Rust のクロージャ |evt| {...} に相当）
            _outfitField.RegisterValueChangedCallback(evt =>
            {
                _outfit = evt.newValue as GameObject; // as は「キャストできなければ null」
                _body = null; // 衣装が変わったらボディも検出し直す
                Refresh();
            });

            _avatarField.objectType = typeof(GameObject);
            _avatarField.SetEnabled(false); // 表示専用（衣装の親から自動で決まる）

            rootVisualElement.Q<Button>("refresh-button").clicked += () =>
            {
                _body = null;
                Refresh();
            };
            _runButton.clicked += Run;

            Refresh();
        }

        // ---- 処理 ----

        private void SetOutfit(GameObject outfit)
        {
            _outfit = outfit;
            _body = null;
            Refresh();
        }

        // 衣装からアバター・ボディ候補を求め直し、UI 全体を更新する
        private void Refresh()
        {
            // CreateGUI がまだ呼ばれていない（UI が無い）ときは何もしない
            if (_meshList == null) return;

            _outfitField.SetValueWithoutNotify(_outfit); // コールバックを発火させずに表示だけ更新
            _bodyContainer.Clear();
            ClearList();

            _avatar = _outfit != null ? BlendshapeSyncBinder.FindAvatarRoot(_outfit) : null;
            _avatarField.SetValueWithoutNotify(_avatar);

            // 早期 return で異常系を先に処理する（Rust の ? 演算子でエラーを先に返すスタイルに近い）
            if (_outfit == null)
            {
                ShowMessage("衣装のルートオブジェクトを指定してください（Hierarchy からドラッグ＆ドロップできます）。", HelpBoxMessageType.Info);
                return;
            }
            if (_avatar == null)
            {
                ShowMessage("衣装の親に VRC Avatar Descriptor が見つかりません。衣装をアバターの中に入れてください。", HelpBoxMessageType.Warning);
                return;
            }
            if (_avatar == _outfit)
            {
                ShowMessage("アバターではなく、衣装のルートオブジェクトを指定してください。", HelpBoxMessageType.Warning);
                return;
            }

            var candidates = BlendshapeSyncBinder.RankBodyCandidates(_avatar, _outfit);
            if (candidates.Count == 0)
            {
                ShowMessage("ボディの候補（シェイプキーを持つメッシュ）が見つかりません。", HelpBoxMessageType.Warning);
                return;
            }

            // 選択中のボディが候補に無ければ（初回・衣装変更・再検出時）、自動検出の結果を使う
            // ?? は「左が null なら右」。Rust の Option::or / unwrap_or に相当
            var selected = candidates.FirstOrDefault(c => c.Renderer == _body)
                           ?? candidates.FirstOrDefault(c => c.Score > 0)
                           ?? candidates[0];
            _body = selected.Renderer;

            // PopupField<T> はジェネリックなドロップダウン。T（ここでは BodyCandidate）をそのまま値として扱える
            // 表示用の文字列は FormatCandidate で作る（Rust の Display 実装を渡すイメージ）
            var bodyPopup = new PopupField<BodyCandidate>("ボディ", candidates, selected, FormatCandidate, FormatCandidate);
            bodyPopup.RegisterValueChangedCallback(evt =>
            {
                _body = evt.newValue.Renderer;
                UpdatePreview();
            });
            _bodyContainer.Add(bodyPopup);

            UpdatePreview();
        }

        // 選択中のボディでプレビューを計算して一覧に表示する
        private void UpdatePreview()
        {
            _messageContainer.Clear();
            var preview = BlendshapeSyncBinder.Preview(_body, _outfit);
            ShowList(preview, isPreview: true);

            var toAdd = preview.Meshes.Sum(m => m.AddedCount);
            _runButton.SetEnabled(toAdd > 0);
            if (preview.Meshes.All(m => m.CommonCount == 0))
                ShowMessage("このボディと共通のシェイプキーを持つ衣装メッシュがありません。ボディの選択を確認してください。", HelpBoxMessageType.Warning);
            else if (toAdd == 0)
                ShowMessage("すべて登録済みです。", HelpBoxMessageType.Info);
        }

        private void Run()
        {
            var result = BlendshapeSyncBinder.Bind(_body, _outfit);
            var total = result.Meshes.Sum(m => m.AddedCount);

            ShowList(result, isPreview: false);
            _messageContainer.Clear();
            ShowMessage($"{total} 個のシェイプキーを登録しました。Ctrl+Z で元に戻せます。", HelpBoxMessageType.Info);
            _runButton.SetEnabled(false); // 実行直後はすべて登録済みなので押せなくする

            LogResult(result);
        }

        // ---- 表示まわり ----

        private static string FormatCandidate(BodyCandidate c) => $"{c.Renderer.name}（共通 {c.Score}）";

        private void ShowMessage(string text, HelpBoxMessageType type)
        {
            _messageContainer.Add(new HelpBox(text, type));
        }

        private void ClearList()
        {
            _listHeader.text = "";
            _meshList.Clear();
            _runButton.SetEnabled(false);
        }

        // 結果（またはプレビュー）を 1 メッシュ 1 行で表示する
        private void ShowList(BindResult result, bool isPreview)
        {
            _listHeader.text = isPreview ? "プレビュー（実行すると以下のように設定されます）" : "実行結果";
            _meshList.Clear();

            foreach (var m in result.Meshes)
            {
                // UXML を使わず C# だけで要素を組み立てることもできる（JS の createElement に相当）
                var row = new VisualElement();
                row.AddToClassList("mesh-row");
                if (m.Status is MeshBindStatus.NoCommonShapes or MeshBindStatus.NoMesh)
                    row.AddToClassList("mesh-row--skip");

                row.Add(new Label(m.Renderer.name));
                var detail = new Label(FormatDetail(m, isPreview));
                detail.AddToClassList("mesh-row__detail");
                row.Add(detail);

                // 行をクリックすると Hierarchy 上のそのメッシュをハイライトする
                var renderer = m.Renderer; // ラムダに渡すためにローカル変数へ（キャプチャ）
                row.RegisterCallback<ClickEvent>(_ => EditorGUIUtility.PingObject(renderer));

                _meshList.Add(row);
            }
        }

        // MenuEntry（Step 3）と同じ内容を Console にも出しておく
        private void LogResult(BindResult result)
        {
            var log = new StringBuilder($"{LogPrefix} 「{_outfit.name}」に設定しました（ボディ: {result.Body.name}）\n");
            foreach (var m in result.Meshes)
                log.AppendLine($"  {m.Renderer.name}: {FormatDetail(m, isPreview: false)}");
            Debug.Log(log.ToString(), _outfit);
        }

        private static string FormatDetail(MeshBindResult m, bool isPreview)
        {
            // switch 式：Rust の match に近い
            return m.Status switch
            {
                MeshBindStatus.Added =>
                    (isPreview ? $"{m.AddedCount} 個追加予定" : $"{m.AddedCount} 個追加")
                    + (m.ComponentCreated ? "・コンポーネント新規" : "")
                    + $"（共通 {m.CommonCount}）",
                MeshBindStatus.UpToDate => $"登録済み（共通 {m.CommonCount}）",
                MeshBindStatus.NoCommonShapes => "共通なし → スキップ",
                MeshBindStatus.NoMesh => "メッシュ未設定 → スキップ",
                _ => m.Status.ToString(), // _ は Rust と同じ「その他すべて」
            };
        }
    }
}
