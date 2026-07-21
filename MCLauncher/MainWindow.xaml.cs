using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MCLauncher {
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Windows.Data;
    using System.Xml;
    using System.Xml.Linq;
    using Windows.ApplicationModel;
    using Windows.Foundation;
    using Windows.Management.Core;
    using Windows.Management.Deployment;
    using Windows.Storage;
    using Windows.System;
    using WPFDataTypes;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, ICommonVersionCommands {
        private const string GDK_SHIM_NAME = @"GDKLaunchShim.exe";
        private const string GDK_DECRYPT_HELPER_NAME = @"GDKDecryptHelper.exe";
        private static readonly string PREFS_PATH = @"preferences.json";
        private static readonly string IMPORTED_VERSIONS_PATH = @"imported_versions";
        private static readonly string VERSIONS_API_UWP = "https://mrarm.io/r/w10-vdb";
        private static readonly string VERSIONS_API_GDK = "https://raw.githubusercontent.com/MinecraftBedrockArchiver/GdkLinks/refs/heads/master/urls.min.json";

        private VersionList _versions;
        public Preferences UserPrefs { get; }

        private HashSet<CollectionViewSource> _versionListViews = new HashSet<CollectionViewSource>();

        private readonly VersionDownloader _anonVersionDownloader = new VersionDownloader();
        private readonly VersionDownloader _userVersionDownloader = new VersionDownloader();
        private readonly Task _userVersionDownloaderLoginTask;
        private volatile int _userVersionDownloaderLoginTaskStarted;
        private volatile bool _hasLaunchTask = false;
        private volatile bool _hasGdkExtractTask = false;

        public MainWindow() {
            Preferences? userPrefs = null;
            if (File.Exists(PREFS_PATH)) {
                userPrefs = JsonConvert.DeserializeObject<Preferences>(File.ReadAllText(PREFS_PATH));
            }
            if (userPrefs == null) {
                UserPrefs = new Preferences();
                RewritePrefs();
            } else {
                UserPrefs = userPrefs;
            }

            var versionsApiUWP = UserPrefs.VersionsApiUWP != "" ? UserPrefs.VersionsApiUWP : VERSIONS_API_UWP;
            var versionsApiGDK = UserPrefs.VersionsApiGDK != "" ? UserPrefs.VersionsApiGDK : VERSIONS_API_GDK;
            _versions = new VersionList("versions_uwp.json", IMPORTED_VERSIONS_PATH, versionsApiUWP, this, VersionEntryPropertyChanged, "versions_gdk.json", versionsApiGDK);

            InitializeComponent();
            DeleteAppxAfterDownloadOption.DataContext = this;
            ShowInstalledVersionsOnlyCheckbox.DataContext = this;

            var versionListViewRelease = Resources["versionListViewRelease"] as CollectionViewSource;
            versionListViewRelease.Filter += new FilterEventHandler((object sender, FilterEventArgs e) => {
                var v = e.Item as Version;
                e.Accepted = v.VersionType == VersionType.Release && (v.IsInstalled || v.IsStateChanging || !(ShowInstalledVersionsOnlyCheckbox.IsChecked ?? false));
            });
            versionListViewRelease.Source = _versions;
            ReleaseVersionList.DataContext = versionListViewRelease;
            _versionListViews.Add(versionListViewRelease);

            var versionListViewBeta = Resources["versionListViewBeta"] as CollectionViewSource;
            versionListViewBeta.Filter += new FilterEventHandler((object sender, FilterEventArgs e) => {
                var v = e.Item as Version;
                e.Accepted = v.VersionType == VersionType.Beta && (v.IsInstalled || v.IsStateChanging || !(ShowInstalledVersionsOnlyCheckbox.IsChecked ?? false));
            });
            versionListViewBeta.Source = _versions;
            BetaVersionList.DataContext = versionListViewBeta;
            _versionListViews.Add(versionListViewBeta);

            var versionListViewPreview = Resources["versionListViewPreview"] as CollectionViewSource;
            versionListViewPreview.Filter += new FilterEventHandler((object sender, FilterEventArgs e) => {
                var v = e.Item as Version;
                e.Accepted = v.VersionType == VersionType.Preview && (v.IsInstalled || v.IsStateChanging || !(ShowInstalledVersionsOnlyCheckbox.IsChecked ?? false));
            });
            versionListViewPreview.Source = _versions;
            PreviewVersionList.DataContext = versionListViewPreview;
            _versionListViews.Add(versionListViewPreview);

            var versionListViewImported = Resources["versionListViewImported"] as CollectionViewSource;
            versionListViewImported.Filter += new FilterEventHandler((object sender, FilterEventArgs e) => {
                var v = e.Item as Version;
                e.Accepted = v.VersionType == VersionType.Imported;
            });

            versionListViewImported.Source = _versions;
            ImportedVersionList.DataContext = versionListViewImported;
            _versionListViews.Add(versionListViewImported);

            _userVersionDownloaderLoginTask = new Task(() => {
                _userVersionDownloader.EnableUserAuthorization();
            });
            Dispatcher.Invoke(LoadVersionList);
        }

        private async void LoadVersionList() {
            _versions.PrepareForReload();

            LoadingProgressLabel.Content = "从缓存加载GDK版本";
            LoadingProgressBar.Value = 1;

            LoadingProgressGrid.Visibility = Visibility.Visible;

            try {
                await _versions.LoadFromCacheGDK();
            } catch (Exception e) {
                Trace.WriteLine("List cache load failed:\n" + e.ToString());
            }

            LoadingProgressLabel.Content = "从缓存加载UWP版本";
            LoadingProgressBar.Value = 2;
            try {
                await _versions.LoadFromCacheUWP();
            } catch (Exception e) {
                Trace.WriteLine("List cache load failed:\n" + e.ToString());
            }

            _versions.PrepareForReload();

            LoadingProgressLabel.Content = "正在下载新的GDK版本数据";
            LoadingProgressBar.Value = 3;
            try {
                await _versions.DownloadVersionsGDK();
            } catch (Exception e) {
                Debug.WriteLine("List download failed:\n" + e.ToString());
                MessageBox.Show("从互联网更新版本列表失败，某些新版本可能缺失。", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }


            LoadingProgressLabel.Content = "正在下载新的UWP版本数据";
            LoadingProgressBar.Value = 4;
            try {
                await _versions.DownloadVersionsUWP();
            } catch (Exception e) {
                Debug.WriteLine("List download failed:\n" + e.ToString());
                MessageBox.Show("从互联网更新版本列表失败，某些新版本可能缺失。", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            LoadingProgressLabel.Content = "正在加载导入的版本";
            LoadingProgressBar.Value = 5;
            await _versions.LoadImported();

            LoadingProgressGrid.Visibility = Visibility.Collapsed;
        }

        private void VersionEntryPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            RefreshLists();
        }

        private async void ImportButtonClicked(object sender, RoutedEventArgs e) {
            Microsoft.Win32.OpenFileDialog openFileDlg = new Microsoft.Win32.OpenFileDialog();
            openFileDlg.Filter = "XVC和APPX包 (*.msixvc, *.appx)|*.msixvc;*.appx|APPX包 (*.appx)|*.appx|XVC包 (*.msixvc)|*.msixvc|所有文件|*.*";
            Nullable<bool> result = openFileDlg.ShowDialog();
            if (result == true) {
                Directory.CreateDirectory(IMPORTED_VERSIONS_PATH);
                string directory = Path.Combine(IMPORTED_VERSIONS_PATH, openFileDlg.SafeFileName);
                if (Directory.Exists(directory)) {
                    var found = false;
                    foreach (var version in _versions) {
                        if (version.IsImported && version.GameDirectory == directory) {
                            if (version.IsStateChanging) {
                                MessageBox.Show("同名版本已导入，目前正在修改中。请稍等片刻，然后重试.", "Error");
                                return;
                            }
                            MessageBoxResult messageBoxResult = System.Windows.MessageBox.Show("已导入同名版本。您想删除它吗？", "Delete Confirmation", System.Windows.MessageBoxButton.YesNo);
                            if (messageBoxResult == MessageBoxResult.Yes) {
                                var uninstallResult = await Remove(version);
                                if (!uninstallResult) {
                                    MessageBox.Show("删除现有版本失败。导入已中止.", "Error");
                                    return;
                                }
                                found = true;
                                break;
                            } else {
                                return;
                            }
                        }
                    }
                    if (!found) {
                        MessageBox.Show("导入的目标路径已存在，并且不包含启动器已知的Minecraft安装。为了避免数据丢失，导入已中止。请手动删除文件.", "Error");
                        return;
                    }
                }

                var extension = Path.GetExtension(openFileDlg.FileName).ToLowerInvariant();
                PackageType packageType;
                if (extension == ".msixvc") {
                    packageType = PackageType.GDK;
                } else if (extension == ".appx") {
                    packageType = PackageType.UWP;
                } else {
                    MessageBox.Show("不支持的文件扩展名: " + extension, "Import failure");
                    return;
                }


                var versionEntry = _versions.AddEntry(openFileDlg.SafeFileName, directory, packageType);
                VersionTabs.SelectedItem = ImportedTab;

                bool success = false;

                //TODO: for now we don't have any way to know whether these are preview builds or not
                if (packageType == PackageType.UWP) {
                    success = await ExtractAppx(openFileDlg.FileName, directory, versionEntry);
                
                } else if (packageType == PackageType.GDK) {
                    if (!ShowGDKFirstUseWarning()) {
                        success = false;
                    } else {
                        success = await ExtractMsixvc(openFileDlg.FileName, directory, versionEntry, isPreview: false);
                    }
                } else {
                    Debug.Assert(false);
                }

                if (success) {
                    versionEntry.StateChangeInfo = null;
                    versionEntry.UpdateInstallStatus();
                } else {
                    _versions.Remove(versionEntry);
                }
            }
        }

        private void InstallError(string userMessage, string debug, string fileName, Exception? ex) {
            string exceptionMessage = "none";
            if (ex != null) {
                Trace.WriteLine(debug + ": " + ex.ToString());
                exceptionMessage = ex.Message;
            } else {
                Trace.WriteLine(debug);
            }

            MessageBox.Show(
                "导入文件失败: " + fileName + "\n\n" +
                userMessage +
                (ex != null ? "\n\n异常消息: " + exceptionMessage : "") +
                "\n\n如果需要更多信息，请检查日志文件（文件->打开日志文件）.", "Import failure"
            );
        }

        private async Task<bool> ExtractAppx(string filePath, string directory, Version versionEntry) {
            versionEntry.StateChangeInfo = new VersionStateChangeInfo(VersionState.Extracting);
            try {
                await Task.Run(() => {
                    ZipFile.ExtractToDirectory(filePath, directory);
                    File.Delete(Path.Combine(directory, "AppxSignature.p7x"));
                });

                versionEntry.UpdateInstallStatus();

                return true;
            } catch (InvalidDataException ex) {
                InstallError(
                    "文件似乎已损坏或不是APPX文件",
                    "Failed extracting appx",
                    filePath,
                    ex
                );
                return false;
            } finally {
                versionEntry.StateChangeInfo = null;
            }
        }

        private bool ShowGDKFirstUseWarning() {
            if (!UserPrefs.HasPreviouslyUsedGDK) {
                var result = MessageBox.Show(
                    "启动器检测到您是首次在此启动器中使用GDK版本的Minecraft。\n" +
                        "请注意以下事项：\n\n" +
                        "您必须先安装Microsoft Store中的GDK版本Minecraft，然后才能使用启动器管理GDK版本。\n" +
                        "这是因为启动器需要Store来安装解密安装包所需的密钥。\n" +
                        "如果不这样做，安装包可能会显示损坏消息。\n\n" +
                        "强烈建议您将C:\\XboxGames（或游戏默认安装位置）添加到Windows Defender的排除列表中，\n" +
                        "否则安装过程将耗时10倍以上。\n\n" +
                        "在安装过程中，您会看到一些对话框和一个PowerShell窗口短暂弹出。\n" +
                        "这是正常现象，是GDK版本安装方法不可避免的结果。\n\n" +
                        "另请注意，从UWP切换到GDK或反之亦然时，您的世界的存储位置将会改变。\n" +
                        "如果您找不到您的世界，可以使用\"工具\"->\"查找我的Minecraft数据\"来定位它们。",
                    "Minecraft GDK warning",
                    MessageBoxButton.OKCancel
                );
                if (result == MessageBoxResult.OK) {
                    UserPrefs.HasPreviouslyUsedGDK = true;
                    RewritePrefs();
                    return true;
                }
                return false;
            }

            return true;
        }

        private void FixGDKManifest(string path) {
            XDocument doc = XDocument.Load(path);
            XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
            XNamespace rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

            var apps = doc.Descendants(ns + "Application");
            foreach (var app in apps) {
                var executable = app.Attribute("Executable");
                //install from older versions will reference the minecraft exe directly, so we need to patch these
                if (executable != null && (executable.Value == "GameLaunchHelper.exe" || executable.Value == "Minecraft.Windows.exe")) {
                    executable.Value = GDK_SHIM_NAME;
                }
            }

            var extensions = doc.Root.Elements(ns + "Extensions").ToList();
            foreach (var ext in extensions) {
                ext.Remove();
            }

            var capabilities = doc.Descendants(ns + "Capabilities");
            var customInstall = capabilities
                .Elements(rescap + "Capability")
                .Where(c => c.Attribute("Name")?.Value == "customInstallActions")
                .ToList();
            foreach (var cap in customInstall) {
                cap.Remove();
            }

            var settings = new XmlWriterSettings();
            settings.Encoding = new UTF8Encoding(false); //no BOM
            settings.Indent = true;
            using (XmlWriter w = XmlWriter.Create(path, settings)) {
                doc.Save(w);
            }
        }


        private void RecursiveCopyDirectory(string from, string to, HashSet<string> skip) {
            Directory.CreateDirectory(to);
            foreach (var source in Directory.EnumerateFiles(from)) {
                if (skip.Contains(source)) {
                    continue;
                }
                string destination = Path.Combine(to, Path.GetFileName(source));
                Trace.WriteLine(source + " -> " + destination);
                File.Copy(source, destination);
            }
            foreach (var source in Directory.EnumerateDirectories(from)) {
                string destination = Path.Combine(to, Path.GetFileName(source));
                RecursiveCopyDirectory(source, destination, skip);
            }
        }

        private async Task<bool> ExtractMsixvc(string filePath, string directory, Version versionEntry, bool isPreview) {
            if (_hasGdkExtractTask) {
                InstallError(
                    "不能同时安装多个MSIXVC包。请等待当前安装完成后再开始新的安装。",
                    "Concurrent MSIXVC installation attempt",
                    filePath,
                    null
                );
                return false;
            }
            _hasGdkExtractTask = true;
            try {
                directory = Path.GetFullPath(directory);
                // XVC are encrypted containers, I don't currently know of any way to extract them to an arbitrary directory
                // For now we just stage the package in XboxGames, and then move the files to the launcher data directory

                versionEntry.StateChangeInfo = new VersionStateChangeInfo(VersionState.Staging);

                var packageManager = new PackageManager();

                //make sure XboxGames is cleared
                Trace.WriteLine("Clearing existing XboxGames Minecraft installation");
                try {
                    await UnregisterPackage(versionEntry.GamePackageFamily, versionEntry, skipBackup: false);
                } catch (Exception ex) {
                    InstallError(
                        "无法删除现有的XboxGames Minecraft安装。请确保Minecraft没有运行然后重试。",
                        "Failed clearing XboxGames Minecraft installation",
                        filePath,
                        ex
                    );
                    return false;
                }

                try {
                    await DeploymentProgressWrapper(packageManager.StagePackageAsync(new Uri(filePath), null), versionEntry);
                } catch (Exception ex) {
                    InstallError(
                        "暂存包失败。\n" +
                            "这可能意味着文件已损坏，或者不是MSIXVC文件。请检查文件完整性。\n\n" +
                            "不过，此错误也可能发生在您从未从Store安装过GDK版本Minecraft的情况下，\n" +
                            "因为启动器依赖Store来安装解密安装包所需的密钥。\n" +
                            "请确保您已从Store安装了" + (isPreview ? "Minecraft Preview" : "Minecraft") + "，然后再使用启动器安装GDK版本。",
                        "Failed staging MSIXVC",
                        filePath,
                        ex
                    );
                    return false;
                }

                string installPath = "";
                foreach (var pkg in new PackageManager().FindPackages(versionEntry.GamePackageFamily)) {
                    if (installPath != "") {
                        InstallError(
                            "Minecraft安装在多个位置，启动器无法确定从何处复制文件。\n" +
                            "这可能是因为其他用户也安装了该游戏。",
                            "Multiple locations found for staged MSIXVC: " + installPath + ", " + pkg.InstalledLocation.Path,
                            filePath,
                            null
                        );
                        return false;
                    }
                    installPath = pkg.InstalledLocation.Path;
                }
                Trace.WriteLine("Detected staging path: " + installPath);
                string resolvedPath = LinkResolver.Resolve(installPath);
                Trace.WriteLine("Symlink resolved as " + resolvedPath);
                installPath = resolvedPath;

                var exeSrcPath = Path.Combine(installPath, "Minecraft.Windows.exe");
                if (!Directory.Exists(installPath)) {
                    InstallError(
                        "未在预期位置找到安装：" + installPath + "\n也许您的XboxGames文件夹位于其他位置？",
                        "Expected XboxGames Minecraft directory not found" + installPath,
                        filePath,
                        null
                    );
                    return false;
                }
                if (!File.Exists(exeSrcPath)) {
                    InstallError(
                        "未找到Minecraft可执行文件：" + exeSrcPath,
                        "Expected XboxGames Minecraft executable not found: " + exeSrcPath,
                        filePath,
                        null
                    );
                    return false;
                }

                versionEntry.StateChangeInfo.VersionState = VersionState.Decrypting;

                var exeTmpDir = Path.GetTempPath();
                var uuid = Guid.NewGuid().ToString();
                //Use a different tmp path to make sure we don't copy half-done files
                //UUID makes sure we don't copy the leftovers of a different, failed installation
                var exeTmpPath = Path.Combine(exeTmpDir, "Minecraft.Windows_" + uuid + ".exe");
                var donePath = exeTmpPath + ".done";

                var exeDstPath = Path.Combine(Path.GetFullPath(directory), "Minecraft.Windows.exe");
                var decryptHelperLogFile = Path.GetTempFileName();

                var helperPath = Path.Combine(Directory.GetCurrentDirectory(), GDK_DECRYPT_HELPER_NAME);

                //TODO: these paths probably need to be escaped
                var command = $@"Invoke-CommandInDesktopPackage `
                            -PackageFamilyName ""{versionEntry.GamePackageFamily}"" `
                            -App Game `
                            -Command \""{helperPath}\"" `
                            -Args '\""{exeSrcPath}\"" \""{exeTmpPath}\"" \""{decryptHelperLogFile}\"" \""{donePath}\""'
                        ";
                Trace.WriteLine("Decrypt command: " + command);

                var processInfo = new ProcessStartInfo {
                    FileName = "powershell.exe",
                    Arguments = command,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };

                Trace.WriteLine("Copying decrypted exe");
                try {
                    var process = Process.Start(processInfo);
                    process.WaitForExit();
                    Trace.WriteLine("Process output:" + process.StandardOutput.ReadToEnd());
                    Trace.WriteLine("Process errors:" + process.StandardError.ReadToEnd());
                } catch (Exception ex) {
                    Trace.WriteLine("Decrypt helper log output: " + File.ReadAllText(decryptHelperLogFile));
                    InstallError(
                        "运行PowerShell将Minecraft可执行文件从暂存包中复制出来失败",
                        "Failed running PowerShell for exe extraction",
                        filePath,
                        ex
                    );
                    return false;
                }

                for (int i = 0; i < 300 && !File.Exists(donePath); i++) {
                    //Give it up to 30 seconds to copy the file
                    //We can't block on the outcome of Invoke-CommandInDesktopPackage, so we have to poll for the file
                    //TODO: What if the copy takes longer than that?
                    await Task.Delay(100);
                }

                Trace.WriteLine("Decrypt helper log output: " + File.ReadAllText(decryptHelperLogFile));

                if (!File.Exists(exeTmpPath)) {
                    Trace.WriteLine("Src path: " + exeSrcPath);
                    Trace.WriteLine("Tmp path: " + exeTmpPath);
                    InstallError(
                        "无法从暂存包中复制Minecraft可执行文件。\n" +
                            "这通常是因为您的Windows用户账户未安装游戏许可证。\n\n" +
                            "请确保您已从Store安装了" + (isPreview ? "Minecraft Preview" : "Minecraft") + "，然后再使用此启动器。",
                        "PowerShell subprocess didn't seem to copy the exe in time",
                        filePath,
                        null
                    );
                    return false;
                }
                Trace.WriteLine("Minecraft executable decrypted successfully");

                versionEntry.StateChangeInfo.VersionState = VersionState.Moving;
                //TODO: this could fail if the launcher is on a different drive than C: ?
                try {
                    Trace.WriteLine("Moving staged files: " + installPath + " -> " + directory);
                    if (Path.GetPathRoot(installPath) == Path.GetPathRoot(directory)) {
                        Trace.WriteLine("Destination for extraction is on the same drive as the installation location - moving files for speed");
                        Directory.Move(installPath, directory);
                    } else {
                        Trace.WriteLine("Destination for extraction is on a different drive than staged - copying files");
                        //Minecraft.Windows.exe can't be copied directly due to permissions
                        HashSet<string> skip = new HashSet<string>();
                        skip.Add(exeSrcPath);
                        RecursiveCopyDirectory(installPath, directory, skip);
                    }


                    Trace.WriteLine("Moving decrypted exe into place");
                    File.Delete(exeDstPath);
                    File.Move(exeTmpPath, exeDstPath);
                } catch (Exception ex) {
                    InstallError(
                        "复制/移动游戏文件到目标文件夹失败",
                        "Failed moving game files to destination",
                        filePath,
                        ex
                    );
                    return false;
                }

                Trace.WriteLine("Cleaning up XboxGames");
                //we already created a backup earlier, so a new attempt would just get in the way
                await UnregisterPackage(versionEntry.GamePackageFamily, versionEntry, skipBackup: true);

                Trace.WriteLine("Done importing msixvc: " + filePath);
                return true;

            } finally {
                _hasGdkExtractTask = false;
            }
        }

        public ICommand LaunchCommand => new RelayCommand((v) => InvokeLaunch((Version)v));

        public ICommand RemoveCommand => new RelayCommand((v) => InvokeRemove((Version)v));

        public ICommand DownloadCommand => new RelayCommand((v) => InvokeDownload((Version)v));

        private void InvokeLaunch(Version v) {
            if (_hasLaunchTask)
                return;
            _hasLaunchTask = true;
            Task.Run(async () => {
                v.StateChangeInfo = new VersionStateChangeInfo(VersionState.MovingData);
                if (!MoveMinecraftData(v.GamePackageFamily, v.PackageType)) {
                    Trace.WriteLine("Data restore error, aborting launch");
                    v.StateChangeInfo = null;
                    _hasLaunchTask = false;
                    return;
                }
                v.StateChangeInfo = new VersionStateChangeInfo(VersionState.Registering);
                string gameDir = Path.GetFullPath(v.GameDirectory);
                try {
                    await ReRegisterPackage(v.GamePackageFamily, gameDir, v);
                } catch (Exception e) {
                    Debug.WriteLine("App re-register failed:\n" + e.ToString());
                    MessageBox.Show("应用重新注册失败：\n" + e.ToString());
                    _hasLaunchTask = false;
                    v.StateChangeInfo = null;
                    return;
                }
                v.StateChangeInfo = new VersionStateChangeInfo(VersionState.Launching);
                try {
                    var pkg = await AppDiagnosticInfo.RequestInfoForPackageAsync(v.GamePackageFamily);
                    if (pkg.Count > 0) {
                        if (pkg.Count > 1) {
                            Trace.WriteLine("Multiple packages found ???");
                        }
                        var result = await pkg[0].LaunchAsync();
                        if (result.ExtendedError != null) {
                            Trace.WriteLine("LaunchAsync didn't throw, but returned an extended error???");
                            throw result.ExtendedError;
                        }
                    } else {
                        throw new Exception("No packages found for package family " + v.GamePackageFamily);
                    }
                    Trace.WriteLine("App launch finished!");
                } catch (Exception e) {
                    Debug.WriteLine("App launch failed:\n" + e.ToString());
                    MessageBox.Show("应用启动失败：\n" + e.ToString());
                    return;
                } finally {
                    _hasLaunchTask = false;
                    v.StateChangeInfo = null;
                }
            });
        }

        private async Task DeploymentProgressWrapper(IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> t, Version? version) {
            TaskCompletionSource<int> src = new TaskCompletionSource<int>();
            t.Progress += (v, p) => {
                Trace.WriteLine("Deployment progress: " + p.state + " " + p.percentage + "%");
            };
            t.Completed += (v, p) => {
                if (p == AsyncStatus.Error) {
                    Trace.WriteLine("Deployment failed: " + v.GetResults().ErrorText + " (error code " + v.GetResults().ExtendedErrorCode.HResult + ")");
                    src.SetException(new Exception("Deployment failed: " + v.GetResults().ErrorText));
                } else {
                    Trace.WriteLine("Deployment done: " + p);
                    src.SetResult(1);
                }
            };
            await src.Task;
        }

        private int GetWorldCountInDataDir(string dataDir) {
            var worldsFolder = Path.Combine(dataDir, "games", "com.mojang", "minecraftWorlds");
            if (!Directory.Exists(worldsFolder)) {
                return 0;
            }
            return Directory.GetDirectories(worldsFolder).Length;
        }

        private Dictionary<string, int> LocateMinecraftWorlds(string packageFamily) {
            List<string> candidates = new List<string>();

            var uwpDataDir = GetMinecraftUWPDataDir(packageFamily);
            if (uwpDataDir != "") {
                candidates.Add(uwpDataDir);
            }

            candidates.AddRange(GetMinecraftGDKDataDirs(packageFamily));
            candidates.Add(GetBackupMinecraftDataDir());

            var worldLocations = new Dictionary<string, int>();
            foreach(var dataDir in candidates) {
                Trace.WriteLine("Checking for worlds in: " + dataDir);
                var worldsFolder = Path.Combine(dataDir, "games", "com.mojang", "minecraftWorlds");
                if (!Directory.Exists(worldsFolder)) {
                    Trace.WriteLine("No worlds found in: " + worldsFolder);
                    continue;
                }
                int worlds = Directory.GetDirectories(worldsFolder).Length;
                if (worlds > 0) {
                    worldLocations[dataDir] = worlds;
                    Trace.WriteLine("Found " + worlds + " worlds in: " + worldsFolder);
                } else {
                    Trace.WriteLine("No worlds found in: " + worldsFolder);
                }
            }

            return worldLocations;
        }

        private string GetBackupMinecraftDataDir() {
            //TODO: this really ought to be separated by package family
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string tmpDir = Path.Combine(localAppData, "TmpMinecraftLocalState");
            return tmpDir;
        }

        private string GetMinecraftUWPRootDir(string packageFamily) {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                packageFamily
            );
        }

        private string GetMinecraftUWPDataDir(string packageFamily) {
            return Path.Combine(GetMinecraftUWPRootDir(packageFamily), "LocalState");
        }

        private string GetMinecraftGDKRootDir(string packageFamily) {
            string infix;
            switch(packageFamily) {
                case MinecraftPackageFamilies.MINECRAFT:
                    infix = "Minecraft Bedrock";
                    break;
                case MinecraftPackageFamilies.MINECRAFT_PREVIEW:
                    infix = "Minecraft Bedrock Preview";
                    break;
                default: throw new ArgumentException("Invalid Minecraft package family: " + packageFamily);
            }
            var gdkRootDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                infix
            );
            return gdkRootDir;
        }

        private List<string> GetMinecraftGDKDataDirs(string packageFamily) {
            var parentDir = Path.Combine(
                GetMinecraftGDKRootDir(packageFamily),
                "Users"
            );
            var results = new List<string>();

            if (!Directory.Exists(parentDir)) {
                Trace.WriteLine("GDK Users directory doesn't exist: " + parentDir);
                return results;
            }

            results.AddRange(Directory.EnumerateDirectories(parentDir));

            return results;
        }

        private bool BackupMinecraftDataForRemoval(string packageFamily) {
            ApplicationData data;
            try {
                data = ApplicationDataManager.CreateForPackageFamily(packageFamily);
            }catch (FileNotFoundException e) {
                Trace.WriteLine("BackupMinecraftDataForRemoval: Application data not found for package family " + packageFamily + ": " + e.ToString());
                Trace.WriteLine("This should mean the package isn't installed, so we don't need to backup the data");
                return true;
            }
            if (!Directory.Exists(data.LocalFolder.Path)) {
                //this is fine only for GDK versions
                Trace.WriteLine("LocalState folder " + data.LocalFolder.Path + " doesn't exist, so it can't be backed up");
                return true;
            }
            string tmpDir = GetBackupMinecraftDataDir();
            if (Directory.Exists(tmpDir)) {
                if (GetWorldCountInDataDir(tmpDir) > 0) {
                    //TODO: this might happen if two different versions
                    //try to be uninstalled at the same time???
                    Trace.WriteLine("BackupMinecraftDataForRemoval error: " + tmpDir + " already exists");
                    Process.Start("explorer.exe", tmpDir);
                    MessageBox.Show("用于备份MC数据的临时目录已存在。这可能意味着我们上次备份数据失败了。请手动备份目录.");
                    return false;
                }
                Directory.Delete(tmpDir, recursive: true);
            }
            Trace.WriteLine("Moving Minecraft data to: " + tmpDir);
            Directory.Move(data.LocalFolder.Path, tmpDir);

            return true;
        }

        private void RestoreMove(string from, string to) {
            foreach (var f in Directory.EnumerateFiles(from)) {
                string ft = Path.Combine(to, Path.GetFileName(f));
                if (File.Exists(ft)) {
                    if (MessageBox.Show("目标" + ft + " 中已存在文件.\n否则，旧文件将丢失.", "Restoring data directory from previous installation", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        continue;
                    File.Delete(ft);
                }
                File.Move(f, ft);
            }
            foreach (var f in Directory.EnumerateDirectories(from)) {
                string tp = Path.Combine(to, Path.GetFileName(f));
                if (!Directory.Exists(tp)) {
                    if (File.Exists(tp) && MessageBox.Show("文件 " + tp + " 不是目录。是否要删除它？否则，旧目录中的数据将丢失.", "Restoring data directory from previous installation", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        continue;
                    Directory.CreateDirectory(tp);
                }
                RestoreMove(f, tp);
            }
        }

        private bool RestoreUWPData(string src, string uwpDataDir, string uwpParent) {
            Trace.WriteLine("Restoring Minecraft data from src dir " + src + " to " + uwpDataDir);
            try {
                if (Directory.Exists(uwpDataDir)) {
                    Trace.WriteLine("Deleting: " + uwpDataDir);
                    Directory.Delete(uwpDataDir, recursive: true);
                }
                if (!Directory.Exists(uwpParent)) {
                    Trace.WriteLine("Creating parent dir: " + uwpParent);
                    Directory.CreateDirectory(uwpParent);
                }
                Trace.WriteLine("Restoring files");
                RestoreMove(src, uwpDataDir);
                Trace.WriteLine("Deleting src dir: " + src);
                Directory.Delete(src, true);
                Trace.WriteLine("Restore complete");
                return true;
            } catch (Exception e) {
                Debug.WriteLine("Failed restoring Minecraft data from " + src + ": " + e.ToString());
                MessageBox.Show("移动Minecraft数据失败，来自：\n"
                    + src
                    + "\n到：\n"
                    + uwpDataDir
                    + "\n\n有关详细信息，请检查日志文件。", "Data restore error"
                );
                return false;
            }
        }

        private bool MoveMinecraftData(string packageFamily, PackageType destinationType) {
            var dataLocations = LocateMinecraftWorlds(packageFamily);
            if (dataLocations.Count == 0) {
                Trace.WriteLine("No Minecraft data found to restore or link");
                return true;
            }

            string gdkRoot = GetMinecraftGDKRootDir(packageFamily);
            string uwpDataDir = GetMinecraftUWPDataDir(packageFamily);

            if (dataLocations.Count > 1) {
                var messageString = "";
                foreach (var loc in dataLocations) {
                    messageString += $"\n - {loc.Key}: {loc.Value} 个世界";
                }
                Debug.WriteLine("Can't automatically restore Minecraft data - multiple locations with worlds found:" + messageString);
                MessageBox.Show(
                    "无法自动恢复UWP的Minecraft世界，因为找到了多个包含世界的文件夹："
                        + messageString
                        + "\n\n请手动将世界复制到目标位置来解决冲突。",
                    "Data restore error"
                );
                return result == MessageBoxResult.OK;
            }

            string dataLocation = dataLocations.Keys.First();

            string tmpDir = GetBackupMinecraftDataDir();

            string uwpParent = GetMinecraftUWPRootDir(packageFamily);
            if (dataLocation == tmpDir) {
                //we don't know where GDK will want to store this due to the user folder names containing some kind of UID
                //so we restore to UWP location and let Minecraft handle the GDK migration by itself
                Trace.WriteLine("Restoring Minecraft data from backup dir " + tmpDir + " to " + uwpDataDir);
                if (!RestoreUWPData(tmpDir, uwpDataDir, uwpParent)) {
                    return false;
                }
                dataLocation = uwpDataDir;
            }

            if (destinationType == PackageType.GDK && dataLocation == uwpDataDir) {
                //TODO: not sure it's a good idea to let the game migrate UWP data on its own,
                //considering how many people have had problems with it???
                Trace.WriteLine("Deleting uwpMigration.dat, so GDK Minecraft will migrate data from UWP next time it's used");
                var uwpMigrationDat = Path.Combine(
                    GetMinecraftGDKRootDir(packageFamily),
                    "games",
                    "com.mojang",
                    "uwpMigration.dat"
                );
                Trace.WriteLine("uwpMigration.dat path: " + uwpMigrationDat);
                try {
                    File.Delete(uwpMigrationDat);
                    return true;
                } catch (Exception e) {
                    Trace.WriteLine("Failed deleting uwpMigration.dat: " + e.ToString());
                    MessageBox.Show(
                        "删除uwpMigration.dat文件失败。\n" +
                        "您的世界将对UWP版本可见，但GDK版本将看不到它们，除非您手动将其移回。\n\n" +
                        "请手动删除以下文件：" + uwpMigrationDat +
                        "\n\n或者，您可以在下次运行GDK版本时将您的世界复制回GDK文件夹。" +
                        "\n您的世界当前位于：" + uwpDataDir +
                        "Data migration notice"
                    );
                    return false;
                }
            } else if (destinationType == PackageType.UWP && dataLocation != uwpDataDir) {
                //this should mean that we found data in a GDK location, so move it for UWP
                var gdkDataDir = dataLocations.Keys.First();
                if (!RestoreUWPData(gdkDataDir, uwpDataDir, uwpParent)) {
                    return false;
                }

                return true;
            } else {
                Trace.WriteLine("Minecraft data already in the right place " + dataLocation);
                return true;
            }
        }

        private async Task RemovePackage(Package pkg, string packageFamily, Version? version, bool skipBackup) {
            Trace.WriteLine("Removing package: " + pkg.Id.FullName);
            if (!pkg.IsDevelopmentMode) {
                if (!skipBackup) {
                    //TODO: It would be nice to skip this if we're uninstalling a GDK version
                    //however, the package being removed may not be the one passed in the version parameter
                    //since the version parameter is only used for displaying UI status
                    if (!BackupMinecraftDataForRemoval(packageFamily)) {
                        throw new Exception("Failed backing up Minecraft data before uninstalling package");
                    }
                }
                //TODO: this will bomb data for other users. We only currently backup data for the current user
                await DeploymentProgressWrapper(new PackageManager().RemovePackageAsync(pkg.Id.FullName, RemovalOptions.RemoveForAllUsers), version);
            } else {
                Trace.WriteLine("Package is in development mode");
                await DeploymentProgressWrapper(new PackageManager().RemovePackageAsync(pkg.Id.FullName, RemovalOptions.PreserveApplicationData | RemovalOptions.RemoveForAllUsers), version);
            }
            Trace.WriteLine("Removal of package done: " + pkg.Id.FullName);
        }

        private string GetPackagePath(Package pkg) {
            try {
                return pkg.InstalledLocation.Path;
            } catch (FileNotFoundException) {
                return "";
            }
        }

        private async Task UnregisterPackage(string packageFamily, Version? version, bool skipBackup) {
            foreach (var pkg in new PackageManager().FindPackages(packageFamily)) {
                string location = GetPackagePath(pkg);
                Trace.WriteLine("Removing package: " + pkg.Id.FullName + " " + location);
                await RemovePackage(pkg, packageFamily, version, skipBackup);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateHardLink(
            string lpFileName,
            string lpExistingFileName,
            IntPtr lpSecurityAttributes
        );

        private async Task ReRegisterPackage(string packageFamily, string gameDir, Version version) {
            Trace.WriteLine("Registering package");
            string manifestPath = Path.Combine(gameDir, "AppxManifest.xml");

            bool updateManifest = false;

            if (version.PackageType == PackageType.GDK) {
                string shimPath = Path.Combine(gameDir, GDK_SHIM_NAME);

                string backupManifestPath = Path.Combine(gameDir, "AppxManifest_original.xml");
                bool hasManifestBackup = File.Exists(backupManifestPath);

                if (!File.Exists(shimPath)) {
                    updateManifest = true;
                    if (hasManifestBackup) {
                        //we need to redo the manifest for older versions that didn't have the shim present
                        File.Copy(backupManifestPath, manifestPath, overwrite: true);
                        Trace.WriteLine("Manifest needs re-patching because GDK launch shim has been added to an old install");
                    } else {
                        Trace.WriteLine("Adding launch shim to new GDK install");
                    }
                } else {
                    Trace.WriteLine("Updating GDK launch shim");
                }

                File.Delete(shimPath);
                if (CreateHardLink(shimPath, GDK_SHIM_NAME, IntPtr.Zero)) {
                    //hardlink is way less annoying for development, in theory
                    Trace.WriteLine("Successfully hardlinked GDK launch shim");
                } else {
                    File.Copy(GDK_SHIM_NAME, shimPath, overwrite: true);
                    Trace.WriteLine("Couldn't create hard link for GDK shim, copying instead");
                }

                //avoid patching the manifest unless necessary, the user might have edited it
                if (updateManifest) {
                    Trace.WriteLine("Patching AppxManifest.xml");
                    if (!hasManifestBackup) {
                        Trace.WriteLine("Backing up original manifest");
                        File.Copy(manifestPath, backupManifestPath);
                    }
                    FixGDKManifest(manifestPath);
                }
            }

            foreach (var pkg in new PackageManager().FindPackages(packageFamily)) {
                string location = GetPackagePath(pkg);
                if (location == gameDir && !updateManifest) {
                    Trace.WriteLine("Skipping package removal - same path: " + pkg.Id.FullName + " " + location);
                    return;
                }
                await RemovePackage(pkg, packageFamily, version, skipBackup: false);
            }

            Trace.WriteLine("Manifest path: " + manifestPath);
            await DeploymentProgressWrapper(new PackageManager().RegisterPackageAsync(new Uri(manifestPath), null, DeploymentOptions.DevelopmentMode), version);
            Trace.WriteLine("App re-register done!");
        }

        private void InvokeDownload(Version v) {
            CancellationTokenSource cancelSource = new CancellationTokenSource();
            v.IsNew = false;
            v.StateChangeInfo = new VersionStateChangeInfo(VersionState.Initializing);
            v.StateChangeInfo.CancelCommand = new RelayCommand((o) => cancelSource.Cancel());

            Trace.WriteLine("Download start");
            Task.Run(async () => {
                string dlPath = Path.GetFullPath((v.VersionType == VersionType.Preview ? "Minecraft-Preview-" : "Minecraft-") + v.Name + (v.PackageType == PackageType.UWP ? ".Appx" : ".msixvc"));
                VersionDownloader downloader = _anonVersionDownloader;

                VersionDownloader.DownloadProgress dlProgressHandler = (current, total) => {
                    if (v.StateChangeInfo.VersionState != VersionState.Downloading) {
                        Trace.WriteLine("Actual download started");
                        v.StateChangeInfo.VersionState = VersionState.Downloading;
                        if (total.HasValue)
                            v.StateChangeInfo.MaxProgress = total.Value;
                    }
                    v.StateChangeInfo.Progress = current;
                };

                try {
                    if (v.PackageType == PackageType.UWP) {
                        await downloader.DownloadAppx(v.UUID, "1", dlPath, dlProgressHandler, cancelSource.Token);
                    } else if (v.PackageType == PackageType.GDK) {
                        if (!ShowGDKFirstUseWarning()) {
                            v.StateChangeInfo = null;
                            v.UpdateInstallStatus();
                            return;
                        }
                        await downloader.DownloadMsixvc(v.DownloadURLs, dlPath, dlProgressHandler, cancelSource.Token);
                    } else {
                        throw new Exception("Unknown package type");
                    }
                    Trace.WriteLine("Download complete");
                } catch (BadUpdateIdentityException) {
                    Trace.WriteLine("Download failed due to failure to fetch download URL");
                    MessageBox.Show(
                        "无法获取版本的下载URL." +
                        (v.VersionType == VersionType.Beta ? "\n对于测试版，请确保您的帐户已订阅Xbox Insider Hub应用程序中的Minecraft测试版计划." : "")
                    );
                    v.StateChangeInfo = null;
                    return;
                } catch (Exception e) {
                    Trace.WriteLine("Download failed:\n" + e.ToString());
                    if (!(e is TaskCanceledException))
                        MessageBox.Show("下载失败:\n" + e.ToString());
                    v.StateChangeInfo = null;
                    return;
                }
                try {
                    string dirPath = v.GameDirectory;
                    if (Directory.Exists(dirPath))
                        Directory.Delete(dirPath, true);
                    if (v.PackageType == PackageType.UWP) {
                        await ExtractAppx(dlPath, dirPath, v);
                    } else if (v.PackageType == PackageType.GDK) {
                        await ExtractMsixvc(dlPath, dirPath, v, isPreview: v.VersionType == VersionType.Preview);
                    } else {
                        throw new Exception("Unknown package type");
                    }
                    if (UserPrefs.DeleteAppxAfterDownload) {
                        Trace.WriteLine("Deleting package to reduce disk usage");
                        File.Delete(dlPath);
                    } else {
                        Trace.WriteLine("Not deleting package due to user preferences");
                    }
                } catch (Exception e) {
                    Debug.WriteLine("Extraction failed:\n" + e.ToString());
                    MessageBox.Show("解压失败：\n" + e.ToString());
                    v.StateChangeInfo = null;
                    return;
                }
                v.StateChangeInfo = null;
                v.UpdateInstallStatus();
            });
        }

        private async Task<bool> Remove(Version v) {
            try {
                v.StateChangeInfo = new VersionStateChangeInfo(VersionState.Unregistering);
                Trace.WriteLine("Unregistering version " + v.DisplayName);
                try {
                    int unregistered = 0;
                    foreach (var pkg in new PackageManager().FindPackages(v.GamePackageFamily)) {
                        string location = GetPackagePath(pkg);
                        if (location == "" || Path.GetFullPath(location) == Path.GetFullPath(v.GameDirectory)) {
                            Trace.WriteLine("Removing package: " + pkg.Id.FullName + " " + location);
                            await RemovePackage(pkg, v.GamePackageFamily, v, skipBackup: false);
                            unregistered++;
                        }
                    }
                    if (unregistered == 0) {
                        Trace.WriteLine($"Looks like {v.GameDirectory} is not registered with the system, no unregistering performed");
                    }
                } catch (Exception e) {
                    Debug.WriteLine("Failed unregistering package:\n" + e.ToString());
                    MessageBox.Show("取消注册包失败：\n" + e.ToString(), "Uninstall error");
                    return false;
                }
                Trace.WriteLine("Cleaning up game files for version " + v.DisplayName);
                v.StateChangeInfo = new VersionStateChangeInfo(VersionState.CleaningUp);
                try {
                    // Use the \\?\ prefix to support long paths
                    Directory.Delete(@"\\?\" + Path.GetFullPath(v.GameDirectory), true);
                } catch (Exception e) {
                    Debug.WriteLine("Failed deleting game directory:\n" + e.ToString());
                    MessageBox.Show("删除游戏目录失败：\n" + e.ToString(), "Uninstall error");
                    return false;
                }

                if (v.IsImported) {
                    Dispatcher.Invoke(() => _versions.Remove(v));
                    Trace.WriteLine("Removed imported version " + v.DisplayName);
                } else {
                    v.UpdateInstallStatus();
                    Trace.WriteLine("Removed release version " + v.DisplayName);
                }

                return true;
            } finally { 
                v.StateChangeInfo = null;
            }
        }

        private void InvokeRemove(Version v) {
            Task.Run(async () => await Remove(v));
        }

        private void ShowInstalledVersionsOnlyCheckbox_Changed(object sender, RoutedEventArgs e) {
            UserPrefs.ShowInstalledOnly = ShowInstalledVersionsOnlyCheckbox.IsChecked ?? false;
            RefreshLists();
            RewritePrefs();
        }

        private void RefreshLists() {
            Dispatcher.Invoke(() => {
                foreach (var list in _versionListViews) {
                    list.View.Refresh();
                }
            });
        }

        private void DeleteAppxAfterDownloadCheck_Changed(object sender, RoutedEventArgs e) {
            UserPrefs.DeleteAppxAfterDownload = DeleteAppxAfterDownloadOption.IsChecked;
            RewritePrefs();
        }

        private void RewritePrefs() {
            File.WriteAllText(PREFS_PATH, JsonConvert.SerializeObject(UserPrefs));
        }

        private void MenuItemOpenLogFileClicked(object sender, RoutedEventArgs e) {
            if (!File.Exists(@"Log.txt")) {
                MessageBox.Show("日志文件未找到", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            } else 
                Process.Start(new ProcessStartInfo
                {
                    FileName = @"Log.txt",
                    UseShellExecute = true
                });
        }

        private void MenuItemOpenDataDirClicked(object sender, RoutedEventArgs e) {
            Process.Start(@"explorer.exe", Directory.GetCurrentDirectory());
        }

        private void MenuItemUninstallAllVersionsClicked(object sender, RoutedEventArgs e) {
            var result = MessageBox.Show(
                "启动器管理的所有Minecraft版本都将被注销和删除.\n" +
                    "您的数据（世界等）不会被删除.\n\n" +
                    "注意：如果您只想从应用商店重新安装《我的世界》，并且不想删除启动器管理的版本，则可以使用“清理以重新安装应用商店”选项.\n\n" +
                    "您确定要继续吗?",
                "Uninstall all versions",
                MessageBoxButton.OKCancel
            );
            if (result == MessageBoxResult.OK) {
                Trace.WriteLine("Starting uninstall of ALL versions!");
                foreach (var version in _versions) {
                    if (version.IsInstalled) {
                        InvokeRemove(version);
                    }
                }
                Trace.WriteLine("Scheduled uninstall of ALL versions.");
            }
        }

        private void MenuItemRefreshVersionListClicked(object sender, RoutedEventArgs e) {
            Dispatcher.Invoke(LoadVersionList);
        }

        private void onEndpointChangedHandler(object sender, string newUwpIdsEndpoint, string newGdkPackageUrlsEndpoint) {
            UserPrefs.VersionsApiUWP = newUwpIdsEndpoint == "" ? VERSIONS_API_UWP : newUwpIdsEndpoint;
            UserPrefs.VersionsApiGDK = newGdkPackageUrlsEndpoint == "" ? VERSIONS_API_GDK : newGdkPackageUrlsEndpoint;
            Dispatcher.Invoke(LoadVersionList);
            RewritePrefs();
        }

        private void MenuItemSetVersionListEndpointClicked(object sender, RoutedEventArgs e) {
            var dialog = new VersionListEndpointDialog(UserPrefs.VersionsApiUWP) {
                Owner = this
            };
            dialog.OnEndpointChanged += onEndpointChangedHandler;

            dialog.Show();
        }

        private string buildDataLocationMessage(string displayName, string packageFamily) {
            var message = displayName + " 的数据：";
            var locations = LocateMinecraftWorlds(packageFamily);
            if (locations.Count == 0) {
                return message + "\n - (未找到包含世界的文件夹)";
            }

            foreach (var loc in locations) {
                message += $"\n - 在 {loc.Key} 中找到 {loc.Value} 个世界";
            }
            return message;
        }

        private void MenuItemFindMyDataClicked(object sender, RoutedEventArgs e) {
            var locations = LocateMinecraftWorlds(MinecraftPackageFamilies.MINECRAFT);

            MessageBox.Show(
                buildDataLocationMessage("Release", MinecraftPackageFamilies.MINECRAFT) + "\n\n" +
                buildDataLocationMessage("Preview", MinecraftPackageFamilies.MINECRAFT_PREVIEW) + "\n\n" +
                "注意：不显示不包含世界的数据文件夹.",
                "Minecraft data locations"
            );
        }

        private async void MenuItemCleanupForStoreInstallClicked(object sender, RoutedEventArgs e) {
            var dialog = new ProgressDialog();
            dialog.Owner = this;
            bool allowClose = false;
            dialog.Closing += (object? sender_, CancelEventArgs e_) => {
                if (!allowClose) {
                    e_.Cancel = true;
                }
            };

            dialog.Show();

            Trace.WriteLine("Cleaning up system");
            try {
                await UnregisterPackage(MinecraftPackageFamilies.MINECRAFT, null, skipBackup: false);
                await UnregisterPackage(MinecraftPackageFamilies.MINECRAFT_PREVIEW, null, skipBackup: false);
            } catch (Exception ex) {
                Debug.WriteLine("Error cleaning up: " + ex.Message);
                MessageBox.Show("清理过程中发生错误。查看日志了解详情。", "Error");
            }
            Trace.WriteLine("Done cleaning up");
            allowClose = true;
            dialog.Close();
            MessageBox.Show("清理已完成。您现在应该可以从Microsoft Store安装Minecraft了.", "Cleanup completed");
        }

        private void ShowBetaTabOption_Changed(object sender, RoutedEventArgs e) {
            UserPrefs.ShowLegacyBetaTab = ShowBetaTabOption.IsChecked;
            BetaTab.Visibility = UserPrefs.ShowLegacyBetaTab ? Visibility.Visible : Visibility.Collapsed;
            if (VersionTabs.SelectedItem == BetaTab && BetaTab.Visibility != Visibility.Visible) {
                VersionTabs.SelectedItem = ReleaseTab;
            }
            RewritePrefs();
        }
    }

    struct MinecraftPackageFamilies
    {
        public const string MINECRAFT = "Microsoft.MinecraftUWP_8wekyb3d8bbwe";
        public const string MINECRAFT_PREVIEW = "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe";
    }

    namespace WPFDataTypes {


        public class NotifyPropertyChangedBase : INotifyPropertyChanged {

            public event PropertyChangedEventHandler? PropertyChanged;

            protected void OnPropertyChanged(string name) {
                if (PropertyChanged != null)
                    PropertyChanged(this, new PropertyChangedEventArgs(name));
            }

        }

        public interface ICommonVersionCommands {

            ICommand LaunchCommand { get; }

            ICommand DownloadCommand { get; }

            ICommand RemoveCommand { get; }

        }

        public enum VersionType : int
        {
            Release = 0,
            Beta = 1,
            Preview = 2,
            Imported = 100
        }

        public enum PackageType {
            UWP,
            GDK
        }

        public class Version : NotifyPropertyChangedBase {
            public static readonly string UNKNOWN_UUID = "UNKNOWN";

            public Version(string uuid, string name, VersionType versionType, bool isNew, ICommonVersionCommands commands, PackageType packageType, List<string>? downloadUrls) {
                this.UUID = uuid;
                this.Name = name;
                this.VersionType = versionType;
                this.IsNew = isNew;
                this.DownloadCommand = commands.DownloadCommand;
                this.LaunchCommand = commands.LaunchCommand;
                this.RemoveCommand = commands.RemoveCommand;
                this.GameDirectory = (versionType == VersionType.Preview ? "Minecraft-Preview-" : "Minecraft-") + Name;
                this.PackageType = packageType;
                this.DownloadURLs = downloadUrls ?? new List<string>();
            }
            public Version(string name, string directory, ICommonVersionCommands commands, PackageType packageType) {
                this.UUID = UNKNOWN_UUID;
                this.Name = name;
                this.VersionType = VersionType.Imported;
                this.DownloadCommand = commands.DownloadCommand;
                this.LaunchCommand = commands.LaunchCommand;
                this.RemoveCommand = commands.RemoveCommand;
                this.GameDirectory = directory;
                this.PackageType = packageType;
                this.DownloadURLs = new List<string>();
            }

            public string UUID { get; set; }
            public string Name { get; set; }
            public VersionType VersionType { get; set; }
            public bool IsNew {
                get { return _isNew; }
                set {
                    _isNew = value;
                    OnPropertyChanged("IsNew");
                }
            }
            public bool IsImported {
                get => VersionType == VersionType.Imported;
            }

            public string GameDirectory { get; set; }

            public PackageType PackageType { get; set; }

            public List<string> DownloadURLs { get; set; }

            public string GamePackageFamily
            {
                get => VersionType == VersionType.Preview ? MinecraftPackageFamilies.MINECRAFT_PREVIEW : MinecraftPackageFamilies.MINECRAFT;
            }

            public bool IsInstalled => Directory.Exists(GameDirectory);

            public string DisplayName {
                get {
                    string typeTag = "";
                    if (VersionType == VersionType.Beta)
                        typeTag = "(测试版)";
                    else if (VersionType == VersionType.Preview)
                        typeTag = "(预览版)";
                    string packageTypeTag = "";
                    if (PackageType == PackageType.GDK) {
                        packageTypeTag += "GDK";
                    } else if (PackageType == PackageType.UWP) {
                        packageTypeTag += "UWP";
                    }

                    return Name + " - " + packageTypeTag + (typeTag.Length > 0 ? " " + typeTag : "") + (IsNew ? " (新!)" : "");
                }
            }
            public string DisplayInstallStatus {
                get {
                    return IsInstalled ? "已安装" : "未安装";
                }
            }

            public ICommand LaunchCommand { get; set; }
            public ICommand DownloadCommand { get; set; }
            public ICommand RemoveCommand { get; set; }

            private VersionStateChangeInfo? _stateChangeInfo;
            private bool _isNew = false;
            public VersionStateChangeInfo? StateChangeInfo {
                get { return _stateChangeInfo; }
                set { _stateChangeInfo = value; OnPropertyChanged("StateChangeInfo"); OnPropertyChanged("IsStateChanging"); }
            }

            public bool IsStateChanging => StateChangeInfo != null;

            public void UpdateInstallStatus() {
                OnPropertyChanged("IsInstalled");
            }

        }

        public enum VersionState {
            Initializing,
            Downloading,
            Extracting,
            Registering,
            Launching,
            Unregistering,
            CleaningUp,
            Staging,
            Decrypting,
            Moving,
            MovingData
        };

        public class VersionStateChangeInfo : NotifyPropertyChangedBase {

            private VersionState _versionState;

            private long _progress = 0;
            private long _maxProgress = 0;

            public VersionStateChangeInfo(VersionState versionState) {
                _versionState = versionState;
            }

            public VersionState VersionState {
                get { return _versionState; }
                set {
                    _versionState = value;
                    Progress = 0;
                    MaxProgress = 0;
                    OnPropertyChanged("IsProgressIndeterminate");
                    OnPropertyChanged("DisplayStatus");
                }
            }

            public bool IsProgressIndeterminate {
                get {
                    return _maxProgress == 0;
                }
            }

            public long Progress {
                get { return _progress; }
                set { _progress = value; OnPropertyChanged("Progress"); OnPropertyChanged("DisplayStatus"); }
            }

            public long MaxProgress {
                get { return _maxProgress; }
                set { _maxProgress = value; OnPropertyChanged("MaxProgress"); OnPropertyChanged("DisplayStatus"); OnPropertyChanged("IsProgressIndeterminate"); }
            }

            public string DisplayStatus {
                get {
                    switch (_versionState) {
                        case VersionState.Initializing: return "准备中...";
                        case VersionState.Downloading:
                            return "下载中... " + (Progress / 1024 / 1024) + "MiB/" + (MaxProgress / 1024 / 1024) + "MiB";
                        case VersionState.Extracting: return "解压中...";
                        case VersionState.Registering: return "正在注册包...";
                        case VersionState.Launching: return "启动中...";
                        case VersionState.Unregistering: return "正在取消注册包...";
                        case VersionState.CleaningUp: return "清理中...";
                        case VersionState.Staging: return "正在暂存包...（这可能需要几分钟）";
                        case VersionState.Decrypting: return "正在复制解密后的Minecraft.Windows.exe...";
                        case VersionState.Moving: return "正在复制其他游戏文件...";
                        case VersionState.MovingData: return "正在恢复Minecraft世界...";
                        default: return "发生了什么？...";
                    }
                }
            }

            private ICommand? _cancelCommand = null;

            public ICommand? CancelCommand {
                get => _cancelCommand;
                set {
                    _cancelCommand = value;
                    OnPropertyChanged("CancelCommand");
                    OnPropertyChanged("HasCancelCommand");
                }
            }

            public bool HasCancelCommand {
                get => CancelCommand != null;
            }

        }

    }
}
