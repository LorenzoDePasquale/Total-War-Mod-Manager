using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Steamworks;

namespace Total_War_Mod_Manager
{

    public partial class SteamCloudManagerWindow : Window
    {
        RemoteStorage remoteStorage;
        SaveFile[] saveFiles;
        uint appID;

        public SteamCloudManagerWindow(uint appID)
        {
            InitializeComponent();
            this.appID = appID;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            GetCloudFiles();
        }

        private void GetCloudFiles()
        {
            listViewSaves.Items.Clear();
            remoteStorage = RemoteStorage.CreateInstance(appID);
            buttonDelete.IsEnabled = buttonDownload.IsEnabled = buttonUpload.IsEnabled = true;
            if (!remoteStorage.IsCloudEnabledForAccount)
            {
                MessageBox.Show("Steam Cloud is disabled for your account.\nYou need to enable it for this program to work.", "Error");
                return;
            }
            if (!remoteStorage.IsCloudEnabledForApp)
            {
                if (MessageBox.Show("Steam Cloud is disabled for this game.\nYou need to enable it for this program to work.\n\nEnable Steam Cloud for this game?", "Enable Steam Cloud?", MessageBoxButton.YesNo, MessageBoxImage.None) == MessageBoxResult.No) return;
                else remoteStorage.IsCloudEnabledForApp = true;
            }
            saveFiles = remoteStorage.GetSaves();
            for (int i = 0; i < saveFiles.Length; i++)
            {
                listViewSaves.Items.Add(saveFiles[i]);
            }
            remoteStorage.GetQuota(out ulong spaceMax, out ulong spaceAvaiable);
            LabelTotalQuota.Content = SaveFile.BytesToString((long)spaceMax);
            spaceMax /= 1000000;
            spaceAvaiable /= 1000000;
            ProgressBarQuota.Maximum = spaceMax;
            ProgressBarQuota.Value = spaceMax - spaceAvaiable;
        }

        private async void buttonDownload_Click(object sender, RoutedEventArgs e)
        {
            if (listViewSaves.SelectedItems.Count > 0)
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.InitialDirectory = Environment.CurrentDirectory;
                saveFileDialog.FileName = Path.GetFileName(saveFiles[listViewSaves.SelectedIndex].Name);
                if (saveFileDialog.ShowDialog() == true)
                {
                    Title = "Steam Cloud Manager - Downloading " + saveFiles[listViewSaves.SelectedIndex].Name;
                    bool succes = await remoteStorage.DownloadFile(saveFiles[listViewSaves.SelectedIndex], saveFileDialog.FileName);
                    if (!succes)
                    {
                        Title = "Steam Cloud Manager";
                        MessageBox.Show("Couldn't download the file", "Error");
                    }
                    else Title = "Steam Cloud Manager - File downloaded! ";
                }
            }
        }

        private async void buttonUpload_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.InitialDirectory = Environment.CurrentDirectory;
            if (openFileDialog.ShowDialog() == true)
            {
                if (new FileInfo(openFileDialog.FileName).Length >= (long)remoteStorage.GetRemainingSpace())
                {
                    Title = "Steam Cloud Manager";
                    MessageBox.Show("Couldn't upload the file.\nNot enough space.", "Error");
                    return;
                }
                Title = "Steam Cloud Manager - Uploading " + Path.GetFileName(openFileDialog.FileName);
                bool succes = await remoteStorage.UploadFile(openFileDialog.FileName);
                if (!succes)
                {
                    Title = "Steam Cloud Manager";
                    MessageBox.Show("Couldn't upload the file", "Error");
                }
                else
                {
                    Title = "Steam Cloud Manager - File uploaded! ";
                    GetCloudFiles();
                }
            }
        }

        private void buttonDelete_Click(object sender, RoutedEventArgs e)
        {
            if (listViewSaves.SelectedItems.Count > 0)
            {
                if (MessageBox.Show(this, "Are you sure you want to delete " + saveFiles[listViewSaves.SelectedIndex].Name + "?", "Confirm deletion", MessageBoxButton.YesNo, MessageBoxImage.None) == MessageBoxResult.No) return;
                bool succes = remoteStorage.DeleteFile(saveFiles[listViewSaves.SelectedIndex]);
                if (!succes)
                {
                    Title = "Steam Cloud Manager";
                    MessageBox.Show("Couldn't delete the file", "Error");
                }
                else
                {
                    Title = "Steam Cloud Manager - File deleted! ";
                    GetCloudFiles();
                }
            }
        }
    }


    internal class SaveFile
    {
        public string Name { get; set; }
        public int Size { get; set; }
        public string FormattedSize
        {
            get { return BytesToString(Size); }
        }
        public DateTime Timestamp { get; set; }

        public static string BytesToString(long byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
            if (byteCount == 0)
                return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num).ToString() + suf[place];
        }
    }

    internal class RemoteStorage
    {
        static RemoteStorage instance;
        static object sync = new object();
        private uint appId;
        internal bool IsDisposed { get; private set; }
        static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        //internal so that we control the creation of a new istance of the class
        internal RemoteStorage(uint appID)
        {
            appId = appID;
            bool init = false;
            init = SteamAPI.Init();
            if (!init) throw new Exception("Cannot initialize Steamworks API.");
        }

        public SaveFile[] GetSaves()
        {
            checkDisposed();
            int fileCount = SteamRemoteStorage.GetFileCount();
            SaveFile[] files = new SaveFile[fileCount];
            for (int i = 0; i < fileCount; ++i)
            {
                int size;
                string name = SteamRemoteStorage.GetFileNameAndSize(i, out size);
                files[i] = new SaveFile()
                {
                    Name = name,
                    Size = size,
                    Timestamp = UnixEpoch.AddSeconds(SteamRemoteStorage.GetFileTimestamp(name)).ToLocalTime()
                };
            }
            return files;
        }

        public bool IsCloudEnabledForAccount
        {
            get
            {
                checkDisposed();
                return SteamRemoteStorage.IsCloudEnabledForAccount();
            }
        }

        public bool IsCloudEnabledForApp
        {
            get
            {
                checkDisposed();
                return SteamRemoteStorage.IsCloudEnabledForApp();
            }
            set
            {
                checkDisposed();
                SteamRemoteStorage.SetCloudEnabledForApp(value);
            }
        }

        // Returns the space used and total space avaiable for the current game, in bytes
        public bool GetQuota(out ulong totalBytes, out ulong availableBytes)
        {
            checkDisposed();
            return SteamRemoteStorage.GetQuota(out totalBytes, out availableBytes);
        }

        public ulong GetRemainingSpace()
        {
            checkDisposed();
            SteamRemoteStorage.GetQuota(out ulong totalBytes, out ulong availableBytes);
            return availableBytes;
        }

        public async Task<bool> DownloadFile(SaveFile file, string path)
        {
            checkDisposed();
            byte[] buffer = new byte[file.Size];
            await Task.Run(new Action(() => SteamRemoteStorage.FileRead(file.Name, buffer, buffer.Length)));
            try
            {
                File.WriteAllBytes(path, buffer);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public bool DeleteFile(SaveFile file)
        {
            checkDisposed();
            return SteamRemoteStorage.FileDelete(file.Name);
        }

        public async Task<bool> UploadFile(string filePath)
        {
            byte[] data;
            try
            {
                data = File.ReadAllBytes(filePath);
            }
            catch (Exception)
            {
                throw new Exception("Couldn't read the file");
            }
            return await Task.Run(new Func<bool>(() => SteamRemoteStorage.FileWrite(System.IO.Path.GetFileName(filePath), data, data.Length)));
        }

        // Used to make sure we still have an active instance
        private void checkDisposed()
        {
            if (IsDisposed) throw new InvalidOperationException("Instance is no longer valid.");
        }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                SteamAPI.Shutdown();
                IsDisposed = true;
            }
        }

        // Method that ensures only one istance at a time can be made
        public static RemoteStorage CreateInstance(uint appID)
        {
            lock (sync) // only one thread at a time can create a new instance; this way we avoid having multiple instances
            {
                if (instance != null)
                {
                    instance.Dispose();
                    instance = null;
                }
                RemoteStorage rs = new RemoteStorage(appID);
                instance = rs;
                return rs;
            }
        }
    }
}