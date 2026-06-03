using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Core;
using Dark.Net;

namespace GUI
{
    public partial class StartDialog : Window
    {
        public string SelectedGame { get; private set; }
        public string SelectedServer { get; private set; }
        public string SelectedVersion { get; private set; }
        public List<string> SelectedCategories { get; private set; } = new();
        public string SelectedMode { get; private set; }
        public string PreviousVersion { get; private set; }
        public string STokenBuildData { get; private set; }

        private string customSophonUrl;

        private string currentPackageId;
        private string currentPassword;
        private string preDownloadPassword;

        private bool allowClose = false;

        public class PackageItem
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public bool IsChecked { get; set; }
        }

        public StartDialog()
        {
            InitializeComponent();
            DarkNet.Instance.SetWindowThemeWpf(this, Theme.Dark);

            ModeCombo.ItemsSource = new[]
            {
                new ComboBoxItem() { Content = "Sophon" },
                new ComboBoxItem() { Content = "Scattered Files" }
            };

            PreviousVersion = null;
            STokenBuildData = "";
            this.Title = "Select Game Options";
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!allowClose)
            {
                System.Windows.Application.Current.Shutdown();
                e.Cancel = true;
            }
            base.OnClosing(e);
        }

        private void ResetGameCombo()
        {
            GameCombo.ItemsSource = null;
            var source = new[]
            {
                new { Name = "Genshin Impact", Icon = "pack://application:,,,/icons/hk4e.png", Id = "hk4e" },
                new { Name = "Honkai: Star Rail", Icon = "pack://application:,,,/icons/hkrpg.png", Id = "hkrpg" },
                new { Name = "Zenless Zone Zero", Icon = "pack://application:,,,/icons/nap.png", Id = "nap" },
                // TODO: add hi3 support
                //new { Name = "Honkai Impact 3rd", Icon = "pack://application:,,,/icons/bh3.png", Id = "bh3" }, // hi3 needs lot of hardcoded stuff cuz it's different
            };

            if (SelectedMode == "Sophon")
            {
                var list = source.ToList();
                list.Add(new { Name = "Custom Sophon URL", Icon = "pack://application:,,,/icons/custom.png", Id = "custom" });
                source = list.ToArray();
            }

            GameCombo.ItemsSource = source;
        }

        #region mode selection
        private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedMode = (string)(ModeCombo.SelectedItem as ComboBoxItem).Content.ToString();

            ResetGameCombo();
            GameCombo.IsEnabled = true;

            ServerCombo.IsEnabled = false;
            ServerCombo.ItemsSource = null;

            VersionCombo.IsEnabled = false;
            VersionCombo.ItemsSource = null;

            CategoryListBox.IsEnabled = false;
            CategoryListBox.ItemsSource = null;

            DiffMode.IsChecked = false;
            DiffMode.IsEnabled = false;

            ConfirmButton.IsEnabled = false;
            PushMotrixButton.IsEnabled = false;
            DownloadAllButton.IsEnabled = false;
        }
        private void ModeHelpButton_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show("Sophon mode is the new method to download files, it is better & faster.\n\nScattered files is the old method, while older it provides content such as full game zip, update zip, and files from versions earlier than when sophon was available, consider it the legacy mode.", "Mode Information", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region game selection
        private async void GameCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GameCombo.SelectedItem == null) return;
            SelectedGame = ((dynamic)GameCombo.SelectedItem).Id;

            ServerCombo.ItemsSource = null;
            VersionCombo.ItemsSource = null;

            CustomSophonTitle.Visibility = Visibility.Hidden;
            CustomSophonUrl.Visibility = Visibility.Hidden;
            CustomSophonUrl.Text = "";
            customSophonUrl = "";
            CheckSophonButton.Visibility = Visibility.Hidden;

            ServerTitle.Visibility = Visibility.Visible;
            ServerCombo.Visibility = Visibility.Visible;

            if (SelectedMode == "Sophon")
            {
                // custom sophon
                if (SelectedGame == "custom")
                {
                    CustomSophonTitle.Visibility = Visibility.Visible;
                    CustomSophonUrl.Visibility = Visibility.Visible;
                    CheckSophonButton.Visibility = Visibility.Visible;

                    ServerTitle.Visibility = Visibility.Hidden;
                    ServerCombo.Visibility = Visibility.Hidden;
                } else
                {
                    ServerCombo.ItemsSource = new[]
                    {
                        new ComboBoxItem() { Content = "OS" },
                        new ComboBoxItem() { Content = "CN" }
                    };

                    VersionCombo.IsEnabled = false;
                }
                ServerCombo.IsEnabled = true;
            } else
            {
                LoadingOverlay.Visibility = Visibility.Visible;
                var versions = await Dispatch.GetDispatchVersions(SelectedGame);
                LoadingOverlay.Visibility = Visibility.Collapsed;

                VersionCombo.ItemsSource = versions;
                VersionCombo.IsEnabled = true;
            }

            CategoryListBox.IsEnabled = false;
            CategoryListBox.ItemsSource = null;

            DiffMode.IsChecked = false;
            DiffMode.IsEnabled = false;

            ConfirmButton.IsEnabled = false;
            PushMotrixButton.IsEnabled = false;
            DownloadAllButton.IsEnabled = false;
        }

        private async void CheckSophonButton_Click(object sender, RoutedEventArgs e)
        {
            LoadingOverlay.Visibility = Visibility.Visible;

            try
            {
                customSophonUrl = CustomSophonUrl.Text;
                var version = await Sophon.CheckBuild(customSophonUrl);
                VersionCombo.ItemsSource = null;
                VersionCombo.ItemsSource = new[] { version };
                VersionCombo.IsEnabled = true;

                CategoryListBox.ItemsSource = null;
                CategoryListBox.IsEnabled = false;

                ConfirmButton.IsEnabled = false;
                PushMotrixButton.IsEnabled = false;
            DownloadAllButton.IsEnabled = false;
            }
            catch
            {
                System.Windows.MessageBox.Show("Failed to fetch sophon build from the provided URL. Make sure it is a /getBuild URL and try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        #endregion

        #region server selection
        private async void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerCombo.SelectedItem == null) return;
            SelectedServer = (ServerCombo.SelectedItem as ComboBoxItem)?.Content.ToString();

            VersionCombo.IsEnabled = false;
            CategoryListBox.IsEnabled = false;
            VersionCombo.ItemsSource = null;
            CategoryListBox.ItemsSource = null;
            DiffMode.IsChecked = false;
            DiffMode.IsEnabled = false;
            ConfirmButton.IsEnabled = false;
            PushMotrixButton.IsEnabled = false;
            DownloadAllButton.IsEnabled = false;

            currentPackageId = null;
            currentPassword = null;
            preDownloadPassword = null;

            LoadingOverlay.Visibility = Visibility.Visible;
            dynamic metaData = await Meta.GetVersions(SelectedGame, SelectedServer);
            var versions = (List<string>)metaData.Item1;
            currentPackageId = (string)metaData.Item2;
            currentPassword = (string)metaData.Item3;
            if ((string)metaData.Item4 != "")
                preDownloadPassword = (string)metaData.Item4;
            LoadingOverlay.Visibility = Visibility.Collapsed;

            VersionCombo.ItemsSource = versions;
            VersionCombo.IsEnabled = true;
        }
        #endregion

        #region version selection
        private async void VersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VersionCombo.SelectedItem == null) return;
            SelectedVersion = VersionCombo.SelectedItem?.ToString();

            CategoryListBox.IsEnabled = false;
            CategoryListBox.ItemsSource = null;
            SelectedCategories.Clear();
            DiffMode.IsChecked = false;
            DiffMode.IsEnabled = false;
            ConfirmButton.IsEnabled = false;
            PushMotrixButton.IsEnabled = false;
            DownloadAllButton.IsEnabled = false;

            PackageItem[] packageItems = Array.Empty<PackageItem>();

            LoadingOverlay.Visibility = Visibility.Visible;
            if (SelectedMode == "Sophon")
            {
                var password = currentPassword;
                if (SelectedVersion.EndsWith(" (pre-download)") && preDownloadPassword != null)
                {
                    password = preDownloadPassword;
                }
                var packages = SelectedGame == "custom" ? await Meta.GetCustomPackages(customSophonUrl) : await Meta.GetPackages(SelectedServer, SelectedVersion, currentPackageId, password);

                packageItems = packages.Select(p =>
                    new PackageItem() { Id = p[0], Name = $"{p[1]} - {p[2]}", IsChecked = false }
                ).ToArray();
            } else
            {
                List<string> packages = await Dispatch.GetPackages(SelectedGame, SelectedVersion);

                packageItems = packages.Select(p =>
                    new PackageItem() { Id = p.ToLower(), Name = p, IsChecked = false }
                ).ToArray();
            }
            LoadingOverlay.Visibility = Visibility.Collapsed;

            CategoryListBox.ItemsSource = packageItems;
            CategoryListBox.IsEnabled = true;
        }
        #endregion

        #region package selection
        private void CategoryCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            var cb = sender as System.Windows.Controls.CheckBox;
            if (cb?.DataContext is not PackageItem item) return;

            item.IsChecked = cb.IsChecked == true;

            bool anyChecked = false;
            foreach (PackageItem pkg in CategoryListBox.ItemsSource)
            {
                if (pkg.IsChecked) { anyChecked = true; break; }
            }

            ConfirmButton.IsEnabled = anyChecked;
            PushMotrixButton.IsEnabled = anyChecked;
            DownloadAllButton.IsEnabled = anyChecked;

            DiffMode.IsChecked = false;
            DiffMode.IsEnabled = false;
            
            if (anyChecked && SelectedMode == "Sophon" && SelectedVersion != VersionCombo.Items[^1])
            {
                DiffMode.IsEnabled = true;
            }
        }
        #endregion

        private void Confirm_Click(object? sender = null, RoutedEventArgs? e = null)
        {
            allowClose = true;
            SelectedVersion = SelectedMode == "Sophon" ? $"{SelectedVersion}.0" : SelectedVersion;
            if (SelectedGame == "custom")
            {
                SelectedServer = customSophonUrl;
            }
            if ((bool)DiffMode.IsChecked)
            {
                PreviousVersion = (string)VersionCombo.Items[VersionCombo.SelectedIndex + 1];
            }

            SelectedCategories.Clear();
            if (CategoryListBox.ItemsSource != null)
            {
                foreach (PackageItem pkg in CategoryListBox.ItemsSource)
                {
                    if (pkg.IsChecked)
                        SelectedCategories.Add(pkg.Id);
                }
            }
            DialogResult = true;
        }

        private async void DownloadAllButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedPackages = new List<PackageItem>();
            foreach (PackageItem pkg in CategoryListBox.ItemsSource)
            {
                if (pkg.IsChecked) selectedPackages.Add(pkg);
            }
            if (selectedPackages.Count == 0) return;

            var folderDialog = new FolderBrowserDialog
            {
                Description = "Select save folder for downloaded files"
            };
            if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            string savePath = folderDialog.SelectedPath;
            string version = SelectedMode == "Sophon" ? $"{SelectedVersion}.0" : SelectedVersion;

            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = "Fetching package manifests...";
            LoadingBar.IsIndeterminate = true;
            LoadingDetailText.Text = "";
            LoadingDetailText.Visibility = Visibility.Visible;

            try
            {
                var allAssets = new List<SophonManifestAssetProperty>();
                string firstDownloadUrl = "";

                foreach (var pkg in selectedPackages)
                {
                    var (manifest, downloadUrl) = SelectedMode == "Sophon"
                        ? await Sophon.GetManifest(SelectedGame, version, SelectedServer, pkg.Id, STokenBuildData)
                        : await Dispatch.GetFiles(SelectedGame, SelectedVersion, pkg.Id);

                    if (string.IsNullOrEmpty(firstDownloadUrl))
                        firstDownloadUrl = downloadUrl;

                    allAssets.AddRange(manifest.Assets);
                }

                if (allAssets.Count == 0)
                {
                    System.Windows.MessageBox.Show("No files found in selected packages.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                LoadingText.Text = $"Downloading {allAssets.Count} files...";
                LoadingBar.IsIndeterminate = false;
                LoadingBar.Minimum = 0;
                LoadingBar.Maximum = 100;
                LoadingBar.Value = 0;

                long totalSize = allAssets.Sum(a => a.AssetSize);
                long totalCompressed = allAssets.Sum(a => a.AssetChunks.Sum(c => (long)c.ChunkSize));

                var progress = new Progress<double>(v =>
                {
                    double pct = totalSize > 0 ? (v / (double)totalSize) * 100 : 0;
                    Dispatcher.Invoke(() =>
                    {
                        LoadingBar.Value = Math.Min(pct, 100);
                        LoadingDetailText.Text = $"Assembling: {pct:F1}% ({v / 1048576.0:F2} / {totalSize / 1048576.0:F2} MB)";
                    });
                });

                var aria2 = new Aria2Downloader();
                aria2.DownloadProgressChanged += (downloaded, speed) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        double pct = totalCompressed > 0 ? (double)downloaded / totalCompressed * 100 : 0;
                        LoadingBar.Value = Math.Min(pct, 100);
                        LoadingDetailText.Text = $"{Utils.FormatSize(downloaded)} / {Utils.FormatSize(totalCompressed)} ({Utils.FormatSize(speed)}/s)";
                    });
                };

                await aria2.DownloadFilesAsync(allAssets, firstDownloadUrl, progress, savePath);

                int verified = 0;
                int failed = 0;
                foreach (var asset in allAssets)
                {
                    string filePath = System.IO.Path.Combine(savePath, asset.AssetName.Replace('/', System.IO.Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(filePath))
                    {
                        string md5 = Utils.GetMd5(await System.IO.File.ReadAllBytesAsync(filePath));
                        if (md5 == asset.AssetHashMd5)
                            verified++;
                        else
                            failed++;
                    }
                    else
                    {
                        failed++;
                    }
                }

                string msg = verified == allAssets.Count
                    ? $"All {verified} files downloaded and verified successfully."
                    : $"Done: {verified} verified, {failed} failed or missing.";
                System.Windows.MessageBox.Show(msg, "Download Complete", MessageBoxButton.OK,
                    failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Download failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                LoadingBar.IsIndeterminate = true;
            }
        }

        private async void PushMotrixButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedPackages = new List<PackageItem>();
            foreach (PackageItem pkg in CategoryListBox.ItemsSource)
            {
                if (pkg.IsChecked) selectedPackages.Add(pkg);
            }

            if (selectedPackages.Count == 0) return;

            var choice = System.Windows.MessageBox.Show(
                $"Push {selectedPackages.Count} package(s) to Motrix?\n\nGenerate an aria2 input file that downloads ALL chunks.\nImport into Motrix (File > Import), then use MeiBrowser's 'Assemble' to build final files.",
                "Push to Motrix", MessageBoxButton.OKCancel, MessageBoxImage.Information);

            if (choice == MessageBoxResult.Cancel) return;

            LoadingOverlay.Visibility = Visibility.Visible;

            try
            {
                var allAssets = new List<(SophonManifestAssetProperty asset, string downloadUrl)>();
                string version = SelectedMode == "Sophon" ? $"{SelectedVersion}.0" : SelectedVersion;

                foreach (var pkg in selectedPackages)
                {
                    var (manifest, downloadUrl) = SelectedMode == "Sophon"
                        ? await Sophon.GetManifest(SelectedGame, version, SelectedServer, pkg.Id, STokenBuildData)
                        : await Dispatch.GetFiles(SelectedGame, SelectedVersion, pkg.Id);

                    foreach (var asset in manifest.Assets)
                        allAssets.Add((asset, downloadUrl));
                }

                GenerateAria2InputFile(allAssets);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async Task PushToMotrixRpc(List<(SophonManifestAssetProperty asset, string downloadUrl)> allAssets)
        {
            string rpcUrl = "http://localhost:16800/jsonrpc";

            using var http = new System.Net.Http.HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

            try
            {
                var versionReq = BuildJsonRpc("aria2.getVersion");
                var versionResp = await http.PostAsync(rpcUrl, new System.Net.Http.StringContent(versionReq, Encoding.UTF8, "application/json"));
                versionResp.EnsureSuccessStatusCode();
                Console.WriteLine("Motrix RPC connected.");
            }
            catch
            {
                var result = System.Windows.MessageBox.Show("Cannot connect to Motrix at localhost:16800.\n\nMake sure Motrix is running and RPC is enabled (Preferences > Advanced > RPC).\n\nGenerate aria2 input file instead?", "Motrix Not Found", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                    GenerateAria2InputFile(allAssets);
                return;
            }

            int pushed = 0;
            foreach (var (asset, downloadUrl) in allAssets)
            {
                if (asset.AssetChunks.Count > 1)
                {
                    Console.WriteLine($"Skipping chunked asset for Motrix: {asset.AssetName}");
                    continue;
                }

                var urls = new List<string>();
                if (asset.AssetChunks.Count == 0)
                {
                    string fileUrl = downloadUrl;
                    if (!fileUrl.EndsWith(asset.AssetName))
                        fileUrl = $"{downloadUrl}/{asset.AssetName}";
                    urls.Add(fileUrl);
                }
                else
                {
                    urls.Add(downloadUrl.Replace("$0", asset.AssetChunks[0].ChunkName));
                }

                var addUriReq = BuildJsonRpc("aria2.addUri", new object[] { urls });
                try
                {
                    var resp = await http.PostAsync(rpcUrl, new System.Net.Http.StringContent(addUriReq, Encoding.UTF8, "application/json"));
                    if (resp.IsSuccessStatusCode)
                    {
                        pushed++;
                        Console.WriteLine($"Pushed to Motrix: {asset.AssetName}");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to push: {asset.AssetName} ({resp.StatusCode})");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error pushing {asset.AssetName}: {ex.Message}");
                }
            }

            System.Windows.MessageBox.Show($"Pushed {pushed}/{allAssets.Count} file(s) to Motrix.\n\nNote: For sophon/chunked files, Motrix will download compressed chunks, not assembled final files.\nUse MeiBrowser's Confirm + Download to get assembled files.",
                "Motrix Push", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void GenerateAria2InputFile(List<(SophonManifestAssetProperty asset, string downloadUrl)> allAssets)
        {
            if (allAssets.Count == 0) return;

            var folderDialog = new FolderBrowserDialog
            {
                Description = "Select folder to save chunks (will create .aria2_temp subdirectory)"
            };

            if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            string tempDir = System.IO.Path.Combine(folderDialog.SelectedPath, ".aria2_temp");
            System.IO.Directory.CreateDirectory(tempDir);

            string inputFile = System.IO.Path.Combine(tempDir, "urls.txt");
            await Aria2Downloader.GenerateChunkInputFile(inputFile, allAssets, tempDir);

            System.Windows.MessageBox.Show($"Aria2 input file saved to:\n{inputFile}\n\nImport into Motrix (File > Import) or run:\naria2c --input-file=\"{inputFile}\"\n\nAfter download completes, use MeiBrowser's 'Assemble from Temp' button to build final files.",
                "Input File Generated", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static string BuildJsonRpc(string method, object? args = null)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = "meibrowser",
                method,
                @params = args ?? new object[0]
            });
            return json;
        }

        private void STokenButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "SToken Build|getBuildWithStokenLogin.json|JSON Files (*.json)|*.json",
                Multiselect = false
            };

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string json = File.ReadAllText(dlg.FileName);
                if (json != null)
                {
                    STokenBuildData = json;
                    SelectedMode = "Sophon";
                    Confirm_Click();
                }
            }
        }
    }

}
