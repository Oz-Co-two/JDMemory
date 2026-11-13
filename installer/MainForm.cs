using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace JDMemoryInstaller
{
    internal sealed class MainForm : Form
    {
        private readonly ModPackage _bundled;
        private ModPackage _package;
        private UpdateClient.UpdateManifest _latest;
        private UpdateState _updateState = UpdateState.Checking;

        private readonly List<TargetStatus> _targets = new List<TargetStatus>();
        private readonly List<BeatSaberLocation> _manual = new List<BeatSaberLocation>();

        private ListView _list;
        private TextBox _detail;
        private Label _packageLabel;
        private LinkLabel _updateLink;
        private Button _installButton;
        private Button _uninstallButton;
        private Button _browseButton;
        private Button _rescanButton;
        private bool _busy;

        private enum UpdateState { Checking, Latest, Downloadable, OpenPage, Failed, Downloaded }

        public MainForm()
        {
            _bundled = ModPackage.LoadBundled();
            _package = _bundled;
            BuildLayout();
            Load += (s, e) =>
            {
                Rescan(null);
                StartUpdateCheck();
            };
        }

        // ---- 画面の組み立て ----------------------------------------------------

        private void BuildLayout()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Yu Gothic UI", 9.5F);
            Text = $"JDMemory インストーラー v{_bundled.Version}";
            ClientSize = new Size(860, 620);
            MinimumSize = new Size(720, 520);
            StartPosition = FormStartPosition.CenterScreen;

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(12),
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var title = new Label
            {
                Text = "JDMemory インストーラー",
                Font = new Font("Yu Gothic UI", 14F, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4),
            };
            var description = new Label
            {
                Text = "Beat Saber（PC 版・MOD 入り）に JDMemory を入れます。下の一覧から入れる先を選んでください。\n"
                     + "見つからない時は「フォルダを選ぶ」で Beat Saber のフォルダ（Beat Saber.exe がある所）を選んでください。",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8),
            };

            _list = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                Dock = DockStyle.Fill,
            };
            _list.Columns.Add("入手元", 170);
            _list.Columns.Add("ゲームの版", 80);
            _list.Columns.Add("JDMemory", 80);
            _list.Columns.Add("前提 MOD", 120);
            _list.Columns.Add("フォルダ", 380);
            _list.SelectedIndexChanged += (s, e) => UpdateDetail();

            var findRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
            _browseButton = new Button { Text = "フォルダを選ぶ…", AutoSize = true };
            _browseButton.Click += (s, e) => BrowseFolder();
            _rescanButton = new Button { Text = "もう一度探す", AutoSize = true };
            _rescanButton.Click += (s, e) => Rescan(SelectedTarget?.Path);
            findRow.Controls.Add(_browseButton);
            findRow.Controls.Add(_rescanButton);

            _detail = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                Height = 130,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window,
                Margin = new Padding(0, 0, 0, 6),
            };

            _packageLabel = new Label { AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
            _updateLink = new LinkLabel { AutoSize = true, Margin = new Padding(0, 2, 0, 6) };
            _updateLink.LinkClicked += (s, e) => OnUpdateLinkClicked();

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 4, 0, 0),
            };
            var closeButton = new Button { Text = "閉じる", AutoSize = true, MinimumSize = new Size(90, 32) };
            closeButton.Click += (s, e) => Close();
            _uninstallButton = new Button { Text = "アンインストール", AutoSize = true, MinimumSize = new Size(120, 32) };
            _uninstallButton.Click += (s, e) => UninstallSelected();
            _installButton = new Button
            {
                Text = "インストール",
                AutoSize = true,
                MinimumSize = new Size(160, 32),
                Font = new Font("Yu Gothic UI", 9.5F, FontStyle.Bold),
            };
            _installButton.Click += (s, e) => InstallSelected();
            buttons.Controls.Add(closeButton);
            buttons.Controls.Add(_uninstallButton);
            buttons.Controls.Add(_installButton);

            var repoLink = new LinkLabel
            {
                Text = "使い方・ソースコード・不具合の報告（GitHub）",
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 0),
            };
            repoLink.LinkClicked += (s, e) => OpenUrl(UpdateClient.RepoUrl);

            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 136F));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(title, 0, 0);
            table.Controls.Add(description, 0, 1);
            table.Controls.Add(_list, 0, 2);
            table.Controls.Add(findRow, 0, 3);
            table.Controls.Add(_detail, 0, 4);
            table.Controls.Add(_packageLabel, 0, 5);
            table.Controls.Add(_updateLink, 0, 6);
            table.Controls.Add(buttons, 0, 7);
            table.Controls.Add(repoLink, 0, 8);
            Controls.Add(table);

            AcceptButton = _installButton;
            CancelButton = closeButton;
            ResumeLayout(false);
            PerformLayout();

            UpdatePackageLabel();
            SetUpdateText("GitHub で新しい版を確認しています…", UpdateState.Checking);
        }

        // ---- 入れる先の一覧 ----------------------------------------------------

        private TargetStatus SelectedTarget =>
            _list.SelectedIndices.Count > 0 ? _targets[_list.SelectedIndices[0]] : null;

        private void Rescan(string selectPath)
        {
            var locations = BeatSaberFinder.FindInstalls();
            foreach (var m in _manual)
            {
                if (!locations.Any(l => SamePath(l.Path, m.Path)))
                {
                    locations.Add(m);
                }
            }

            _targets.Clear();
            _targets.AddRange(locations.Select(TargetStatus.Inspect));

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var t in _targets)
            {
                var item = new ListViewItem(t.SourceLabel);
                item.SubItems.Add(t.GameVersion ?? "不明");
                item.SubItems.Add(t.InstalledVersion != null ? "v" + t.InstalledVersion : "なし");
                item.SubItems.Add(t.RequiredModsLabel);
                item.SubItems.Add(t.Path);
                _list.Items.Add(item);
            }
            _list.EndUpdate();

            int index = 0;
            if (selectPath != null)
            {
                int found = _targets.FindIndex(t => SamePath(t.Path, selectPath));
                index = found >= 0 ? found : 0;
            }
            if (_targets.Count > 0)
            {
                _list.Items[index].Selected = true;
                _list.Items[index].Focused = true;
                _list.EnsureVisible(index);
            }
            UpdateDetail();
        }

        private static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        private void BrowseFolder()
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "Beat Saber のフォルダ（Beat Saber.exe がある所）を選んでください。中のフォルダや1つ上のフォルダを選んでも探します。",
                ShowNewFolderButton = false,
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                var found = BeatSaberFinder.Resolve(dialog.SelectedPath);
                if (found.Count == 0)
                {
                    MessageBox.Show(this,
                        "選んだフォルダの周りに Beat Saber.exe が見つかりませんでした。\n\n" + dialog.SelectedPath,
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                foreach (var p in found)
                {
                    if (!_manual.Any(m => SamePath(m.Path, p)))
                    {
                        _manual.Add(new BeatSaberLocation { Path = p, Source = "manual" });
                    }
                }
                Rescan(found[0]);
            }
        }

        private void UpdateDetail()
        {
            var t = SelectedTarget;
            if (t == null)
            {
                _detail.Text = _targets.Count == 0
                    ? "Beat Saber が見つかりませんでした。「フォルダを選ぶ」で Beat Saber のフォルダを選んでください。"
                    : "上の一覧から入れる先を選んでください。";
                RefreshButtons();
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("フォルダ: " + t.Path);
            sb.AppendLine("入手元: " + t.SourceLabel);
            string gameNote = _package.SupportsGame(t.GameVersion)
                ? "（動作を確かめた版です）"
                : $"　※ 動作を確かめた版は {_package.GameVersionsText} です";
            sb.AppendLine("ゲームの版: " + (t.GameVersion ?? "不明") + gameNote);
            sb.AppendLine("MOD を読み込む仕組み（BSIPA）: " + (t.HasBsipa
                ? "入っています"
                : "入っていません → 先に BSManager などで MOD を入れてください"));
            sb.AppendLine("前提 MOD: BSML " + (t.HasBsml ? "あり" : "なし") + " / SiraUtil " + (t.HasSiraUtil ? "あり" : "なし")
                + (t.HasRequiredMods ? "" : " → BSManager の MOD の画面で入れてください"));
            sb.AppendLine("JDMemory: " + (t.InstalledVersion != null ? "v" + t.InstalledVersion + " が入っています" : "入っていません")
                + (t.PendingVersion != null ? $"（ゲームの中で v{t.PendingVersion} を準備済み。次の起動で入れ替わります。ここで入れ直すと、準備した物は消します）" : ""));
            sb.Append("履歴: " + (t.HasHistory ? "あり（アンインストールしても残せます）" : "なし"));
            _detail.Text = sb.ToString();
            RefreshButtons();
        }

        private void RefreshButtons()
        {
            var t = SelectedTarget;
            _browseButton.Enabled = !_busy;
            _rescanButton.Enabled = !_busy;
            _list.Enabled = !_busy;

            if (t == null || !t.HasBsipa || _busy)
            {
                _installButton.Enabled = false;
                _installButton.Text = "インストール";
                _uninstallButton.Enabled = t?.InstalledVersion != null && !_busy;
                return;
            }

            _installButton.Enabled = true;
            string target = "v" + _package.Version;
            int cmp = CompareVersions(t.InstalledVersion, _package.Version);
            if (t.InstalledVersion == null)
            {
                _installButton.Text = target + " をインストール";
            }
            else if (cmp < 0)
            {
                _installButton.Text = target + " に更新";
            }
            else if (cmp == 0)
            {
                _installButton.Text = target + " を入れ直す";
            }
            else
            {
                _installButton.Text = target + " に戻す";
            }
            _uninstallButton.Enabled = t.InstalledVersion != null;
        }

        /// <summary>a と b の版を比べる（a が無い・読めない時は小さいとみなす）</summary>
        private static int CompareVersions(string a, string b)
        {
            var va = ModPackage.ParseVersion(a);
            var vb = ModPackage.ParseVersion(b);
            if (va == null)
            {
                return vb == null ? 0 : -1;
            }
            return vb == null ? 1 : va.CompareTo(vb);
        }

        // ---- 入れる・外す ------------------------------------------------------

        private bool ConfirmGameClosed()
        {
            if (!Installer.IsGameRunning())
            {
                return true;
            }
            MessageBox.Show(this,
                "Beat Saber が起動しています。ゲームを終了してから、もう一度押してください。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private bool Ask(string message)
        {
            return MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
                   == DialogResult.Yes;
        }

        private void InstallSelected()
        {
            var t = SelectedTarget;
            if (t == null || !t.HasBsipa || !ConfirmGameClosed())
            {
                return;
            }
            if (!t.HasRequiredMods)
            {
                string missing = string.Join(" と ", new[] { t.HasBsml ? null : "BSML", t.HasSiraUtil ? null : "SiraUtil" }.Where(x => x != null));
                if (!Ask($"{missing} が入っていないため、このままでは JDMemory は読み込まれません。\n"
                         + "先に BSManager の MOD の画面で入れてください。\n\nそれでも JDMemory を入れますか？"))
                {
                    return;
                }
            }
            if (!_package.SupportsGame(t.GameVersion))
            {
                if (!Ask($"このゲームの版（{t.GameVersion ?? "不明"}）では動作を確かめていません（確かめた版: {_package.GameVersionsText}）。\n"
                         + "読み込まれない・正しく動かないことがあります。\n\nそれでも入れますか？"))
                {
                    return;
                }
            }
            if (CompareVersions(t.InstalledVersion, _package.Version) > 0)
            {
                if (!Ask($"今入っている v{t.InstalledVersion} より古い v{_package.Version} に戻します。よろしいですか？"))
                {
                    return;
                }
            }

            try
            {
                Installer.Install(t, _package);
            }
            catch (UnauthorizedAccessException)
            {
                ShowWriteError(t, "権限がありません");
                return;
            }
            catch (IOException e)
            {
                ShowWriteError(t, e.Message);
                return;
            }

            Rescan(t.Path);
            MessageBox.Show(this,
                $"JDMemory v{_package.Version} を入れました。\n\n"
                + "ゲームを起動すると、曲を選ぶ画面の左のパネルの「MODS」に「JDMemory」のタブが出ます。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UninstallSelected()
        {
            var t = SelectedTarget;
            if (t == null || t.InstalledVersion == null || !ConfirmGameClosed())
            {
                return;
            }
            if (MessageBox.Show(this, "JDMemory を外しますか？\n\n" + t.Path, Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            bool deleteData = false;
            if (t.HasHistory)
            {
                var answer = MessageBox.Show(this,
                    "これまでの履歴（譜面ごとの JD・RT と結果の記録）と JDMemory の設定も消しますか？\n\n"
                    + "はい: 消す\nいいえ: 残す（入れ直すとそのまま使えます）",
                    Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (answer == DialogResult.Cancel)
                {
                    return;
                }
                deleteData = answer == DialogResult.Yes;
            }

            try
            {
                Installer.Uninstall(t, deleteData);
            }
            catch (UnauthorizedAccessException)
            {
                ShowWriteError(t, "権限がありません");
                return;
            }
            catch (IOException e)
            {
                ShowWriteError(t, e.Message);
                return;
            }

            Rescan(t.Path);
            MessageBox.Show(this,
                deleteData ? "JDMemory と履歴を消しました。" : "JDMemory を外しました（履歴は残してあります）。",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowWriteError(TargetStatus t, string reason)
        {
            MessageBox.Show(this,
                "書き込めませんでした（" + reason + "）。\n\n"
                + "・Beat Saber や MOD を入れるアプリが動いていたら閉じてから、もう一度試してください。\n"
                + "・それでもだめな時は、このインストーラーを右クリック →「管理者として実行」で開き直してください。\n\n"
                + t.PluginsDir,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // ---- 新しい版 ----------------------------------------------------------

        private void UpdatePackageLabel()
        {
            _packageLabel.Text = $"入れる版: v{_package.Version}（"
                + (_package.Bundled ? "このインストーラーに同梱" : "GitHub から取得・照合済み")
                + $"）　動作を確かめたゲームの版: {_package.GameVersionsText}";
        }

        private void SetUpdateText(string text, UpdateState state)
        {
            _updateState = state;
            _updateLink.Text = text;
            bool clickable = state == UpdateState.Downloadable || state == UpdateState.OpenPage;
            _updateLink.LinkArea = clickable ? new LinkArea(0, text.Length) : new LinkArea(0, 0);
        }

        private async void StartUpdateCheck()
        {
            try
            {
                _latest = await Task.Run(() => UpdateClient.CheckLatest());
            }
            catch (UpdateClient.UpdateException e)
            {
                SetUpdateText("新しい版を確認できませんでした（" + e.Message + "）。このインストーラーの版で入れられます。", UpdateState.Failed);
                return;
            }
            catch (Exception)
            {
                SetUpdateText("新しい版を確認できませんでした。このインストーラーの版で入れられます。", UpdateState.Failed);
                return;
            }

            if (CompareVersions(_latest.Version, _bundled.Version) <= 0)
            {
                SetUpdateText("このインストーラーの版が最新です。", UpdateState.Latest);
            }
            else if (UpdateClient.CanDownload(_latest))
            {
                SetUpdateText($"新しい版 {_latest.Version} があります。ここを押すと GitHub から取得して、その版を入れられるようにします。", UpdateState.Downloadable);
            }
            else
            {
                SetUpdateText($"新しい版 {_latest.Version} があります。ここを押すと GitHub のページを開きます（新しいインストーラーを入手してください）。", UpdateState.OpenPage);
            }
        }

        private async void OnUpdateLinkClicked()
        {
            if (_updateState == UpdateState.OpenPage)
            {
                OpenUrl(UpdateClient.ReleasesUrl);
                return;
            }
            if (_updateState != UpdateState.Downloadable || _latest == null)
            {
                return;
            }

            _busy = true;
            RefreshButtons();
            SetUpdateText($"{_latest.Version} を取得しています…", UpdateState.Checking);
            try
            {
                var latest = _latest;
                _package = await Task.Run(() => UpdateClient.Download(latest));
                UpdatePackageLabel();
                SetUpdateText($"{_latest.Version} を GitHub から取得し、公開された物と一致することを確かめました。", UpdateState.Downloaded);
            }
            catch (Exception e)
            {
                string reason = e is UpdateClient.UpdateException ? e.Message : "予期しないエラー";
                SetUpdateText($"新しい版 {_latest.Version} があります。ここを押すと、もう一度取得します。", UpdateState.Downloadable);
                MessageBox.Show(this, "新しい版を取得できませんでした（" + reason + "）。\nこのインストーラーの版なら、このまま入れられます。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _busy = false;
                UpdateDetail();
            }
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception)
            {
                MessageBox.Show(this, url, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
