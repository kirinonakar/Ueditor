using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Windows.ApplicationModel.Activation;

namespace Ueditor
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = "UeditorSingleInstanceMutex";
        private static readonly string IpcDir = Path.Combine(Path.GetTempPath(), "Ueditor", "IPC");
        private Window? _window;
        private static Mutex? _singleInstanceMutex;
        private FileSystemWatcher? _ipcWatcher;

        public App()
        {
            ApplyLanguageSettings();
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);

            if (!createdNew)
            {
                var cmdArgs = Environment.GetCommandLineArgs();
                if (cmdArgs.Length > 1)
                {
                    Directory.CreateDirectory(IpcDir);
                    var ipcFile = Path.Combine(IpcDir, $"ipc_{Guid.NewGuid():N}.txt");
                    File.WriteAllLines(ipcFile, cmdArgs.Skip(1));
                }
                return;
            }

            StartIpcWatcher();

            _window = new MainWindow();
            _window.Activate();
        }

        private void StartIpcWatcher()
        {
            try
            {
                Directory.CreateDirectory(IpcDir);
                _ipcWatcher = new FileSystemWatcher(IpcDir, "ipc_*.txt")
                {
                    NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };
                _ipcWatcher.Created += OnIpcFileCreated;
            }
            catch { }
        }

        private void OnIpcFileCreated(object sender, FileSystemEventArgs e)
        {
            try
            {
                string[] lines = File.ReadAllLines(e.FullPath);
                foreach (var line in lines)
                {
                    string path = line.Trim().Trim('"', '\'');
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        if (_window is MainWindow mainWindow)
                        {
                            mainWindow.DispatcherQueue.TryEnqueue(() =>
                            {
                                _ = mainWindow.LoadFileIntoTabAsync(path);
                            });
                        }
                    }
                }
                try { File.Delete(e.FullPath); } catch { }
            }
            catch { }
        }

        private void ApplyLanguageSettings()
        {
            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string settingsDir = System.IO.Path.Combine(userProfile, ".ueditor");
                string settingsFilePath = System.IO.Path.Combine(settingsDir, "settings.json");

                if (System.IO.File.Exists(settingsFilePath))
                {
                    string json = File.ReadAllText(settingsFilePath);
                    using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json))
                    {
                        if (doc.RootElement.TryGetProperty("Language", out System.Text.Json.JsonElement langProp))
                        {
                            string lang = langProp.GetString() ?? "Default";
                            if (lang == "Default" || string.IsNullOrEmpty(lang))
                            {
                                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "";
                            }
                            else
                            {
                                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang;
                                
                                // Robustly sync .NET culture variables to enforce thread-level locale override
                                var culture = new System.Globalization.CultureInfo(lang);
                                System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
                                System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
                                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
                                System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to apply language settings: {ex.Message}");
            }
        }
    }
}
