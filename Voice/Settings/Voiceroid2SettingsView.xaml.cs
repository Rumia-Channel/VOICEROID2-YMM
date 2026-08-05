using System.Windows;
using System.Windows.Controls;
using Voiceroid2Ymm.Voice.AITalk;

namespace Voiceroid2Ymm.Voice.Settings
{
    /// <summary>
    /// VOICEROID2-YMM の設定 WPF UserControl。
    /// 上部: 接続情報 (インストール先・シード状態・声質一覧の更新)
    /// 下部: 読み仮名辞書エディタ
    /// バインド元の設定インスタンス (Voiceroid2VoiceSettings) を DataContext に設定して使う。
    /// デザインは VoicePeak-plus の設定画面を踏襲している。
    /// </summary>
    public partial class Voiceroid2SettingsView : UserControl
    {
        public Voiceroid2SettingsView()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshStatus();
            DataContextChanged += (_, _) => RefreshStatus();
        }

        /// <summary>
        /// 環境変数と自動検出の結果を読み取り専用表示へ反映する。
        /// シード値そのものは表示しない (設定済みかどうかのみ)。
        /// </summary>
        void RefreshStatus()
        {
            try
            {
                string install = AITalkInstallation.DetectInstallDirectory();
                InstallDirText.Text = install;
                InstallDirText.Foreground = System.Windows.Media.Brushes.Green;
            }
            catch (Exception ex)
            {
                InstallDirText.Text = $"(未検出: {FirstLine(ex.Message)})";
                InstallDirText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }

            try
            {
                UserDirText.Text = AITalkInstallation.GetUserDataDirectory();
                UserDirText.Foreground = System.Windows.Media.Brushes.Green;
            }
            catch (Exception ex)
            {
                UserDirText.Text = $"(未検出: {FirstLine(ex.Message)})";
                UserDirText.Foreground = System.Windows.Media.Brushes.OrangeRed;
            }

            SeedStatusText.Text = AITalkInstallation.EnvValue(AITalkInstallation.EnvAuthSeed) is null
                ? $"未設定 (環境変数 {AITalkInstallation.EnvAuthSeed})"
                : $"設定済み (環境変数 {AITalkInstallation.EnvAuthSeed})";
            SeedStatusText.Foreground = System.Windows.Media.Brushes.Green;

            if (DataContext is Voiceroid2VoiceSettings settings)
            {
                VoiceCountText.Text = settings.VoiceNames.Length > 0
                    ? $"{settings.VoiceNames.Length} 件 ({string.Join(", ", settings.VoiceNames)})"
                    : "(未取得。下の「声質一覧を更新」を押してください)";
            }
        }

        static string FirstLine(string text)
        {
            int index = text.IndexOf('\n');
            return index < 0 ? text : text.Substring(0, index);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshStatus();
        }

        private async void UpdateVoicesButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not Voiceroid2VoiceSettings settings) return;
            try
            {
                await settings.UpdateVoicesAsync();
                RefreshStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), ex.Message, "VOICEROID2", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not Voiceroid2VoiceSettings settings) return;
            settings.ReadingEntries.Add(new ReadingEntry
            {
                Surface = string.Empty,
                Reading = string.Empty,
                Enabled = true,
            });
            settings.Save();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not Voiceroid2VoiceSettings settings) return;
            if (EntriesGrid.SelectedItem is ReadingEntry entry)
            {
                settings.ReadingEntries.Remove(entry);
                settings.Save();
            }
        }

        /// <summary>
        /// セル編集終了時に空行エントリを削除し、変更を永続化する。
        /// </summary>
        private void EntriesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (DataContext is not Voiceroid2VoiceSettings settings) return;
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row?.Item is not ReadingEntry entry) return;

            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (!entry.IsValid)
                    settings.ReadingEntries.Remove(entry);
                settings.Save();
            }));
        }
    }
}
