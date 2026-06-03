using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Core;
using Dark.Net;

namespace GUI
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<FileItem> RootItems { get; set; }
        public FileItem RootItem { get; set; }

        //
        private string game;
        private string version;
        private string region;
        private List<string> categoryIds = new();
        private string mode;
        private string previousVersion;
        private string stokenData;
        //
        private List<SophonManifestAssetProperty> toDownload = new();
        private long downloadSize = 0;
        private string downloadUrl = "";
        private bool useAria2 = false;

        private bool _updatingCheckStates = false;

        private string appVersion = "1.1";

        public MainWindow()
        {
            InitializeComponent();
            DarkNet.Instance.SetWindowThemeWpf(this, Theme.Dark);

            ConsoleHelper.AllocConsole();
            var handle = ConsoleHelper.GetConsoleWindow();
            ConsoleHelper.ShowWindow(handle, ConsoleHelper.SW_SHOW);
            ConsoleHelper.SetConsoleTitle("Console");

            Console.WriteLine($"Hello World !");

            RootItems = new ObservableCollection<FileItem> {};
            RootItem = new FileItem("root", 0);
            DataContext = this;

            this.WindowState = WindowState.Maximized;
            this.Title = $"MeiBrowser v{appVersion} - @Escartem <3";
        }

        #region package selection
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await Task.Run(CleanupStaleTempDirs);
                await ShowPopup();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start: {ex.Message}\n\nPlease check your network connection and try again.",
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ShowPopup()
        {
            RootItems.Clear();
            RootItem.Children.Clear();
            RootItem.SizeInBytes = 0;
            RootItem.ElementsCount = 0;
            downloadUrl = "";
            toDownload.Clear();
            downloadSize = 0;

            var popup = new StartDialog();
            popup.Owner = this;
            popup.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (popup.ShowDialog() == true)
            {
                game = popup.SelectedGame;
                region = popup.SelectedServer;
                version = popup.SelectedVersion;
                categoryIds = popup.SelectedCategories;
                mode = popup.SelectedMode;
                previousVersion = popup.PreviousVersion;
                stokenData = popup.STokenBuildData;
                Console.WriteLine($"Selected: {game}, {region}, {version}, [{string.Join(", ", categoryIds)}] as {mode} (using prev as {previousVersion})");
                await UpdateFiles();
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPopup();
        }
        #endregion

        #region display files
        private async Task UpdateFiles()
        {
            LoadingOverlay.Visibility = Visibility.Visible;

            try
            {
                SophonManifestProto mergedManifest = new SophonManifestProto();
                string firstDownloadUrl = "";

                var cats = categoryIds.Count > 0 ? categoryIds : new List<string> { "" };

                foreach (var catId in cats)
                {
                    var (manifest, buildDownloadUrl) = mode == "Sophon"
                        ? await Sophon.GetManifest(game, version, region, catId, stokenData)
                        : await Dispatch.GetFiles(game, version, catId);

                    if (string.IsNullOrEmpty(firstDownloadUrl))
                    {
                        firstDownloadUrl = buildDownloadUrl;
                        downloadUrl = buildDownloadUrl;
                    }

                    SophonManifestProto diffedManifest = new SophonManifestProto();
                    if (previousVersion != null)
                    {
                        var (prevManifest, prevDownloadUrl) = await Sophon.GetManifest(game, $"{previousVersion}.0", region, catId);
                        var prevMap = new Dictionary<string, string>();
                        foreach (var asset in prevManifest.Assets)
                        {
                            prevMap[asset.AssetName] = asset.AssetHashMd5;
                        }

                        foreach (var asset in manifest.Assets)
                        {
                            if (!prevMap.ContainsKey(asset.AssetName))
                            {
                                diffedManifest.Assets.Add(asset);
                                continue;
                            }

                            if (prevMap[asset.AssetName] != asset.AssetHashMd5)
                            {
                                diffedManifest.Assets.Add(asset);
                                continue;
                            }
                        }
                    }
                    else
                    {
                        foreach (var asset in manifest.Assets)
                        {
                            diffedManifest.Assets.Add(asset);
                        }
                    }

                    foreach (var asset in diffedManifest.Assets)
                        mergedManifest.Assets.Add(asset);
                }

                if (mergedManifest.Assets.Count == 0)
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    MessageBox.Show("No files found in selected package(s).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    await ShowPopup();
                    return;
                }

                foreach (var asset in mergedManifest.Assets)
                    AddFileToRoot(asset);

                
                foreach (var item in RootItem.Children)
                    RootItem.SizeInBytes += item.SizeInBytes;

                RootItems.Add(RootItem);
            }
            finally
            {
                foreach (var root in RootItems)
                    SortTree(root);
                
                foreach (var item in RootItem.Children)
                {
                    if (item.Type == "Folder")
                        RootItem.ElementsCount += item.ElementsCount;
                    else
                        RootItem.ElementsCount += 1;
                }

                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void SortTree(FileItem node)
        {
            if (node.Children.Count == 0) return;

            var sorted = node.Children
                .OrderByDescending(f => f.Type == "Folder")
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            node.Children.Clear();
            foreach (var c in sorted)
                node.Children.Add(c);

            foreach (var c in node.Children)
                SortTree(c);
        }

        private void AddFileToRoot(SophonManifestAssetProperty asset)
        {
            var path = asset.AssetName;
            var size = asset.AssetSize;
            var parts = path.Split('/', '\\');
            var currentList = RootItem.Children;
            FileItem? parent = null;

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                bool isFile = i == parts.Length - 1;

                var existing = currentList.FirstOrDefault(x => x.Name == part);
                if (existing == null)
                {
                    existing = new FileItem(part, size, parent, asset);
                    currentList.Add(existing);
                }

                FileItem? node = existing;
                node = node.Parent;
                while (node != null)
                {
                    node.SizeInBytes += isFile ? size : 0;
                    if (isFile)
                        node.ElementsCount += 1;
                    node = node.Parent;
                }

                currentList = existing.Children;
                parent = existing;
            }
        }
        #endregion

        #region download files
        private void FileCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_updatingCheckStates) return;

            var cb = sender as CheckBox;
            if (cb?.DataContext is not FileItem item) return;

            bool isChecked = cb.IsChecked == true;

            try
            {
                _updatingCheckStates = true;
                SetChildrenChecked(item, isChecked);
            }
            finally
            {
                _updatingCheckStates = false;
                UpdateParentCheckState(item.Parent);
            }
        }

        private static void SetChildrenChecked(FileItem parent, bool isChecked)
        {
            foreach (var child in parent.Children)
            {
                child.IsChecked = isChecked;
                SetChildrenChecked(child, isChecked);
            }
        }

        private static void UpdateParentCheckState(FileItem? parent)
        {
            if (parent == null) return;

            if (parent.Children.Count == 0)
            {
                UpdateParentCheckState(parent.Parent);
                return;
            }

            bool allChecked = parent.Children.All(c => c.IsChecked);

            parent.IsChecked = allChecked;

            UpdateParentCheckState(parent.Parent);
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var child in RootItem.Children)
            {
                child.IsChecked = true;
                SetChildrenChecked(child, true);
            }
        }

        private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var child in RootItem.Children)
            {
                child.IsChecked = false;
                SetChildrenChecked(child, false);
            }
        }
        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            toDownload.Clear();
            downloadSize = 0;

            var uniqueSet = new HashSet<SophonManifestAssetProperty>();
            CollectCheckedFiles(RootItem, uniqueSet);
            toDownload.AddRange(uniqueSet);

            if (toDownload.Count == 0)
            {
                MessageBox.Show("No files selected. Please check the files you want to download.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"You are about to download {toDownload.Count} file(s) ({Utils.FormatSize(downloadSize)}), continue ?", "Continue?", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog();
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string folderPath = dialog.SelectedPath;
                    await StartDownload(folderPath);
                }
            }
        }

        private void CollectCheckedFiles(FileItem node, HashSet<SophonManifestAssetProperty> uniqueSet)
        {
            if (node.IsChecked)
            {
                if (node.Type == "File")
                {
                    if (uniqueSet.Add(node.SourceFile))
                        downloadSize += node.SizeInBytes;
                }
                else
                {
                    AddFolderFiles(node, uniqueSet);
                }
                return;
            }

            foreach (var child in node.Children)
                CollectCheckedFiles(child, uniqueSet);
        }

        private void AddFolderFiles(FileItem node, HashSet<SophonManifestAssetProperty> uniqueSet)
        {
            foreach (var child in node.Children)
            {
                if (child.Type == "File")
                {
                    if (uniqueSet.Add(child.SourceFile))
                        downloadSize += child.SizeInBytes;
                }
                else
                {
                    AddFolderFiles(child, uniqueSet);
                }
            }
        }

        private async Task StartDownload(string savePath)
        {
            DownloadingOverlay.Visibility = Visibility.Visible;
            DownloadBar.Value = 0;

            var progress = new Progress<double>(v =>
            {
                Dispatcher.Invoke(() =>
                {
                    double percent = (double)(v / downloadSize) * 100;
                    DownloadBar.Value = Math.Min(percent, 100);
                    DownloadText.Text = $"Assembling: {percent:F1}% ({v / 1048576.0:F2} / {downloadSize / 1048576.0:F2} MB)";
                });
            });

            if (useAria2)
            {
                long totalCompressed = toDownload.Sum(a => a.AssetChunks.Sum(c => c.ChunkSize));
                var aria2 = new Aria2Downloader();
                aria2.DownloadProgressChanged += (downloaded, speed) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        double pct = totalCompressed > 0 ? (double)downloaded / totalCompressed * 100 : 0;
                        DownloadBar.Value = Math.Min(pct, 100);
                        DownloadText.Text = $"{Utils.FormatSize(downloaded)} / {Utils.FormatSize(totalCompressed)} ({Utils.FormatSize(speed)}/s)";
                    });
                };
                await aria2.DownloadFilesAsync(toDownload, downloadUrl, progress, savePath);
            }
            else
            {
                var downloader = new Download();
                await downloader.DownloadFilesAsync(toDownload, downloadUrl, progress, savePath);
            }

            DownloadingOverlay.Visibility = Visibility.Collapsed;
        }

        private void Aria2CheckBox_Changed(object sender, RoutedEventArgs e)
        {
            useAria2 = Aria2CheckBox.IsChecked == true;
            Console.WriteLine($"aria2 mode: {useAria2}");
        }

        private async void MotrixButton_Click(object sender, RoutedEventArgs e)
        {
            toDownload.Clear();
            var uniqueSet = new HashSet<SophonManifestAssetProperty>();
            CollectCheckedFiles(RootItem, uniqueSet);
            toDownload.AddRange(uniqueSet);

            if (toDownload.Count == 0)
            {
                MessageBox.Show("No files selected.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool isChunked = toDownload.Any(a => a.AssetChunks.Count > 0);
            if (isChunked)
            {
                GenerateAria2InputFile();
            }
            else
            {
                var choice = MessageBox.Show(
                    $"Push {toDownload.Count} file(s) to Motrix?\n\nYes = Send directly to Motrix via RPC (port 16800)\nNo = Generate aria2 input file",
                    "Push to Motrix", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (choice == MessageBoxResult.Cancel) return;
                if (choice == MessageBoxResult.Yes)
                    await PushToMotrixRpc();
                else
                    GenerateAria2InputFile();
            }
        }

        private async Task PushToMotrixRpc()
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
                var result = MessageBox.Show("Cannot connect to Motrix at localhost:16800.\n\nMake sure Motrix is running and RPC is enabled (Preferences > Advanced > RPC).\n\nGenerate aria2 input file instead?", "Motrix Not Found", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                    GenerateAria2InputFile();
                return;
            }

            int pushed = 0;
            foreach (var asset in toDownload)
            {
                if (asset.AssetChunks.Count > 1)
                {
                    Console.WriteLine($"Skipping chunked asset for Motrix: {asset.AssetName} (use Download button instead)");
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

            MessageBox.Show($"Pushed {pushed}/{toDownload.Count} file(s) to Motrix.\n\nNote: For sophon/chunked files, Motrix will download compressed chunks, not assembled final files.\nUse MeiBrowser's Download button to get assembled files.",
                "Motrix Push", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void GenerateAria2InputFile()
        {
            if (toDownload.Count == 0) return;

            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder to save chunks (will create .aria2_temp subdirectory)"
            };

            if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            string tempDir = System.IO.Path.Combine(folderDialog.SelectedPath, ".aria2_temp");
            System.IO.Directory.CreateDirectory(tempDir);

            string inputFile = System.IO.Path.Combine(tempDir, "urls.txt");
            var assetList = toDownload.Select(a => (a, downloadUrl)).ToList();
            await Aria2Downloader.GenerateChunkInputFile(inputFile, assetList, tempDir);

            MessageBox.Show($"Aria2 input file saved to:\n{inputFile}\n\nImport into Motrix (File > Import) or run:\naria2c --input-file=\"{inputFile}\"\n\nAfter download completes, use MeiBrowser's 'Assemble from Temp' button to build final files.",
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

        private static string[] GetBaseSearchPaths()
        {
            return new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            };
        }

        private async void AssembleButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder containing .aria2_temp with downloaded chunks"
            };

            if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            string selectedPath = folderDialog.SelectedPath;
            string tempDir;

            if (System.IO.Path.GetFileName(selectedPath) == ".aria2_temp")
            {
                tempDir = selectedPath;
            }
            else
            {
                tempDir = System.IO.Path.Combine(selectedPath, ".aria2_temp");
            }

            if (!System.IO.Directory.Exists(tempDir))
            {
                MessageBox.Show("No .aria2_temp directory found at the selected location.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string savePath = System.IO.Path.GetDirectoryName(tempDir) ?? tempDir;

            toDownload.Clear();
            downloadSize = 0;
            var uniqueSet = new HashSet<SophonManifestAssetProperty>();
            CollectCheckedFiles(RootItem, uniqueSet);
            toDownload.AddRange(uniqueSet);

            if (toDownload.Count == 0)
            {
                MessageBox.Show("No files selected in the tree to assemble.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DownloadingOverlay.Visibility = Visibility.Visible;
            DownloadBar.Value = 0;

            try
            {
                var progress = new Progress<double>(v =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        double percent = toDownload.Sum(a => a.AssetSize) > 0 ? (double)(v / toDownload.Sum(a => a.AssetSize)) * 100 : 0;
                        DownloadBar.Value = Math.Min(percent, 100);
                        DownloadText.Text = $"Assembling: {percent:F1}%";
                    });
                });

                await Aria2Downloader.AssembleOnlyAsync(toDownload, tempDir, progress, savePath);
                MessageBox.Show("Assembly complete.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Assembly failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                DownloadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will delete all .aria2_temp cache directories in common download locations (Desktop, Downloads, Documents). Continue?",
                "Clear Cache", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            int deleted = 0;
            foreach (var basePath in GetBaseSearchPaths())
            {
                if (!System.IO.Directory.Exists(basePath)) continue;
                try
                {
                    foreach (var dir in System.IO.Directory.GetDirectories(basePath, ".aria2_temp", System.IO.SearchOption.AllDirectories))
                    {
                        try
                        {
                            Core.Utils.DeleteDirectoryRobust(dir);
                            deleted++;
                            Console.WriteLine($"Cleaned up: {dir}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Could not delete {dir}: {ex.Message}");
                        }
                    }
                }
                catch { }
            }

            MessageBox.Show(
                deleted > 0 ? $"Cleaned up {deleted} cache director{(deleted == 1 ? "y" : "ies")}." : "No cache directories found.",
                "Clear Cache", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static void CleanupStaleTempDirs()
        {
            foreach (var basePath in GetBaseSearchPaths())
            {
                if (!System.IO.Directory.Exists(basePath)) continue;
                try
                {
                    foreach (var dir in System.IO.Directory.GetDirectories(basePath, ".aria2_temp", System.IO.SearchOption.AllDirectories))
                    {
                        try { Core.Utils.DeleteDirectoryRobust(dir); Console.WriteLine($"Cleaned stale cache: {dir}"); }
                        catch { }
                    }
                }
                catch { }
            }
        }

        #endregion
    }

    public class FileItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isChecked;

        public string Name { get; set; }
        public string Type => Children.Count == 0 ? "File" : "Folder";
        public ObservableCollection<FileItem> Children { get; set; } = new();
        public long SizeInBytes { get; set; }
        public string Size => Utils.FormatSize(SizeInBytes);
        public string Icon => Type == "File" ? "pack://application:,,,/icons/file2.png" : "pack://application:,,,/icons/folder.png";
        public SophonManifestAssetProperty SourceFile { get; set; }
        public string Elements => ElementsCount == 0 ? "" : $"{ElementsCount.ToString("# ##0")} files";
        public long ElementsCount { get; set; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsChecked)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public FileItem? Parent { get; set; }

        public FileItem(string name, long sizeInBytes, FileItem? parent = null, SophonManifestAssetProperty? sourceFile = null)
        {
            Name = name;
            SizeInBytes = sizeInBytes;
            Parent = parent;
            SourceFile = sourceFile;
            ElementsCount = 0;
        }
    }
}