using Steamworks;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Total_War_Mod_Manager
{
    public partial class MainWindow : Window
    {
        CallResult<SteamUGCQueryCompleted_t> itemNameQueryResult;
        ObservableModList modList = new ObservableModList();
        bool loadingProfile = true;
        Timer runCallbacksTimer, monitorSubscribtionsTimer, monitorVotesTimer; //These are declared here to avoid them being deleted by the GC
        TaskFactory taskFactory;
        List<BackgroundWorker> bwList = new List<BackgroundWorker>();

        //Loads settings
        public MainWindow()
        {
            InitializeComponent();
            rangeBarTreeView.DataContext = this;
            if (File.Exists("profiles.xml"))
                Settings.LoadSettings("profiles.xml");
            else Settings.CreateDefault();
            foreach (var profile in Settings.Profiles)
            {
                comboBoxProfile.Items.Add(profile.Name);
            }
            checkBoxStartWithSteam.IsChecked = new FileInfo("launcher\\launcher.exe").Length < 1000000;
        }

        //Called when the Steam item query returns asynchronously
        private void OnSendQueryUGCRequest(SteamUGCQueryCompleted_t param, bool bIOFailure)
        {
            if (param.m_eResult == EResult.k_EResultOK)
            {
                UGCQueryHandle_t UGCHandle = param.m_handle;
                SteamUGC.GetQueryUGCResult(UGCHandle, 0, out SteamUGCDetails_t itemDetails);
                SteamUGC.GetQueryUGCPreviewURL(UGCHandle, 0, out string url, 200);
                bool enabled = Settings.CurrentProfile.EnabledMods.Contains(itemDetails.m_rgchTitle);
                string modDirectory = $@"..\..\workshop\content\594570\{itemDetails.m_nPublishedFileId.m_PublishedFileId}";
                Mod mod = new Mod(itemDetails.m_nPublishedFileId.m_PublishedFileId, itemDetails.m_rgchTitle, "" /*itemDetails.m_pchFileName.Remove(0, 5)*/)
                {
                    Enabled = enabled,
                    ImageUrl = url,
                    Description = itemDetails.m_rgchDescription,
                    FilePath = Directory.GetFiles(modDirectory).Where(f => f.EndsWith(".pack")).First()
                };
                if (!modList.Contains(mod.ID))
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        modList.Insert(0, mod);
                    });
                }
                //If the mod is not installed or it needs to be updated, downloads it and installs it
                /*string path = $"{AppDomain.CurrentDomain.BaseDirectory}data\\{mod.PackName}";
                if (!File.Exists(path) || DateTimeOffset.FromUnixTimeSeconds(itemDetails.m_rtimeUpdated) > new FileInfo(path).LastWriteTime)
                {
                    mod.Downloading = true;
                    BackgroundWorker bwDownloadMod = new BackgroundWorker();
                    bwList.Add(bwDownloadMod);
                    bwDownloadMod.ProgressChanged += (sender, e) =>
                    {
                        //ulong is used since int32 is not big enough
                        ulong total = (ulong)((int[])e.UserState)[1];
                        ulong downloaded = (ulong)((int[])e.UserState)[0];
                        int progress = (int)(100 * downloaded / total);
                        mod.Progress = progress;
                    };
                    bwDownloadMod.RunWorkerCompleted += (sender, e) =>
                    {
                        //Terminates the download operation
                        mod.Downloading = false;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            bwList.Remove(bwDownloadMod);
                            listViewMods.ItemTemplateSelector = new ListViewModItemTemplateSelector();
                        });
                    };
                    bwDownloadMod.WorkerReportsProgress = true;
                    path = $@"C:\Program Files (x86)\Steam\userdata\{SteamUser.GetSteamID().GetAccountID().m_AccountID}\ugc\temp\{itemDetails.m_hFile.m_UGCHandle}\mods\{mod.PackName}";
                    if (!(Directory.Exists(path) && File.Exists(path)))
                    {
                        path = $@"C:\Program Files (x86)\Steam\userdata\{SteamUser.GetSteamID().GetAccountID().m_AccountID}\ugc\referenced\{itemDetails.m_hFile.m_UGCHandle}\mods\{mod.PackName}";
                    }
                    if (File.Exists(path))
                    {
                        bwDownloadMod.DoWork += (sender, e) =>
                        {
                            MovePackFile(path, mod.PackName);
                        };
                        bwDownloadMod.RunWorkerAsync();
                        return;
                    }
                    SteamAPICall_t apiCall = SteamRemoteStorage.UGCDownload(itemDetails.m_hFile, 0);
                    CallResult<RemoteStorageDownloadUGCResult_t> callResult = CallResult<RemoteStorageDownloadUGCResult_t>.Create(onDownload);
                    callResult.Set(apiCall, onDownload);
                    bwDownloadMod.DoWork += (sender, e) =>
                    {
                        int bytesDownloaded, bytesTotal;
                        do
                        {
                            SteamRemoteStorage.GetUGCDownloadProgress(itemDetails.m_hFile, out bytesDownloaded, out bytesTotal);
                            BackgroundWorker bw = (BackgroundWorker)sender;
                            if (bytesTotal > 0) bw.ReportProgress(0, new int[] { bytesDownloaded, bytesTotal });
                            Thread.Sleep(50);
                        } while (bytesDownloaded == 0 || bytesDownloaded != bytesTotal);
                        MovePackFile(path, mod.PackName);
                    };
                    bwDownloadMod.RunWorkerAsync();
                }*/
            }

            //Empty method, required as parameter for steam call
            void onDownload(RemoteStorageDownloadUGCResult_t _param, bool _bIOFailure)
            {
            }

            //Moves the pack file to the game folder; removes first 8 bytes because reasons
            void MovePackFile(string from, string packName)
            {
                while (IsFileLocked(new FileInfo(from))) Thread.Sleep(20);
                using (var input = File.Open(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (var reader = new BinaryReader(input))
                    {
                        using (var output = File.Create($"{AppDomain.CurrentDomain.BaseDirectory}data\\{packName}"))
                        {
                            for (int i = 0; i < 8; i++) //Skips first 8 bytes
                                reader.ReadByte();
                            var buffer = new byte[4096];
                            do
                            {
                                var actual = reader.Read(buffer, 0, buffer.Length);
                                output.Write(buffer, 0, actual);
                            } while (reader.BaseStream.Position != reader.BaseStream.Length);
                        }
                    }
                }
            }

            //Check if the file is currently locked
            bool IsFileLocked(FileInfo file)
            {
                FileStream stream = null;
                try
                {
                    stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None);
                }
                catch (IOException)
                {
                    return true;
                }
                finally
                {
                    if (stream != null) stream.Close();
                }
                //file is not locked
                return false;
            }
        }

        //Starts the Steam API and retrives the subscribed mods of the currently logged in user
        private void MainWindow1_Loaded(object sender, RoutedEventArgs e)
        {
            if (!SteamAPI.Init())
            {
                if (checkBoxStartWithSteam.IsChecked == true)
                    Process.Start("steam://rungameid/" + File.ReadAllText("steam_appid.txt"));
                else MessageBox.Show("Steam must be running in order to use this program!");
                Closed -= MainWindow1_Closed;
                Close();
            }
            else
            {
                //Links the visibility of the tree view range bar to its scrollbar
                ScrollViewer sv = FindChild<ScrollViewer>(treeViewModFiles);
                DependencyPropertyDescriptor.FromProperty(ScrollViewer.ComputedVerticalScrollBarVisibilityProperty, typeof(ScrollViewer)).AddValueChanged(sv, (s, _e) =>
                {
                    rangeBarTreeView.Visibility = sv.ComputedVerticalScrollBarVisibility;
                });
                Title += " - Logged in as " + SteamFriends.GetPersonaName();
                //Gets an array of all the subscribed mods by the user
                uint subItemCount = SteamUGC.GetNumSubscribedItems();
                PublishedFileId_t[] subItemList = new PublishedFileId_t[subItemCount];
                SteamUGC.GetSubscribedItems(subItemList, subItemCount);
                //For each mod, makes a query to the steam server for all the mod info
                foreach (var item in subItemList)
                {
                    PublishedFileId_t[] itemID = new PublishedFileId_t[1] { item };
                    UGCQueryHandle_t itemQuery = SteamUGC.CreateQueryUGCDetailsRequest(itemID, 1);
                    SteamAPICall_t itemCall = SteamUGC.SendQueryUGCRequest(itemQuery);
                    itemNameQueryResult = CallResult<SteamUGCQueryCompleted_t>.Create(OnSendQueryUGCRequest);
                    itemNameQueryResult.Set(itemCall, OnSendQueryUGCRequest);
                }
                //Keeps running all the callbacks in background
                runCallbacksTimer = new Timer((argument) => SteamAPI.RunCallbacks(), null, 0, 30);
                //Monitors for changes in the subscribed items
                monitorSubscribtionsTimer = new Timer((argument) =>
                {
                    uint newSubItemCount = SteamUGC.GetNumSubscribedItems();
                    if (modList.Count != newSubItemCount)
                    {
                        PublishedFileId_t[] newSubItemList = new PublishedFileId_t[newSubItemCount];
                        SteamUGC.GetSubscribedItems(newSubItemList, newSubItemCount);
                        if (modList.Count > newSubItemCount) //Removed item
                        {
                            List<ulong> subItemIDList = newSubItemList.Select(x => x.m_PublishedFileId).ToList();
                            for (int i = modList.Count - 1; i >= 0; i--) //Iterates in reverse to allow removing an item during the loop
                            {
                                if (!subItemIDList.Contains(modList[i].ID)) //If the item has been removed
                                {
                                    Dispatcher.Invoke(() => modList.RemoveAt(i));
                                }
                            }
                        }
                        else //Added item
                        {
                            foreach (var item in newSubItemList)
                            {
                                if (!modList.Contains(item.m_PublishedFileId)) //If the item is new
                                {
                                    PublishedFileId_t[] itemID = new PublishedFileId_t[1] { item };
                                    UGCQueryHandle_t itemQuery = SteamUGC.CreateQueryUGCDetailsRequest(itemID, 1);
                                    SteamAPICall_t itemCall = SteamUGC.SendQueryUGCRequest(itemQuery);
                                    itemNameQueryResult = CallResult<SteamUGCQueryCompleted_t>.Create(OnSendQueryUGCRequest);
                                    itemNameQueryResult.Set(itemCall, OnSendQueryUGCRequest);
                                    break;
                                }
                            }
                        }
                    }
                }, null, Timeout.Infinite, 1000);
                //Monitors for changes in the user votes
                monitorVotesTimer = new Timer((argument) =>
                {
                    foreach (var mod in modList)
                    {
                        SteamAPICall_t itemVoteCall = SteamUGC.GetUserItemVote(new PublishedFileId_t(mod.ID));
                        var voteQueryResult = CallResult<GetUserItemVoteResult_t>.Create(OnSendVoteQueryUGCRequest);
                        voteQueryResult.Set(itemVoteCall, OnSendVoteQueryUGCRequest);
                    }
                }, null, 0, 1000);
                //Waits for all the mods info to be obtained, then loads the current settings 
                taskFactory = new TaskFactory();
                taskFactory.StartNew(() =>
                {
                    while (modList.Count != SteamUGC.GetNumSubscribedItems()) //Waits for the complete mod list to be obtained
                        Thread.Sleep(30);
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        //Sets the current profile to the last used one; this counts as a "profile change", so it will load all the profile settings (this invokes "OnModListObtained")
                        comboBoxProfile.SelectedIndex = Settings.CurrentProfileIndex + 1;
                        //If every mod is enabled, checks the "Enable All" checkbox
                        bool allChecked = true;
                        foreach (Mod item in modList)
                        {
                            if (!item.Enabled) allChecked = false;
                        }
                        if (allChecked)
                        {
                            checkBoxEnableAll.Checked -= checkBoxEnableAll_Checked;
                            checkBoxEnableAll.IsChecked = true;
                            checkBoxEnableAll.Checked += checkBoxEnableAll_Checked;
                        }
                        modList.CollectionChanged += ModList_CollectionChanged;
                        //Starts monitoring for new subscribed items
                        monitorSubscribtionsTimer.Change(0, 1000);
                    }, DispatcherPriority.Background);
                });
            }

            T FindChild<T>(DependencyObject parent) where T : DependencyObject
            {
                T foundChild = null;
                int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < childrenCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    T childType = child as T;
                    if (childType == null)
                    {
                        foundChild = FindChild<T>(child);
                        if (foundChild != null) break;
                    }
                    else
                    {
                        foundChild = (T)child;
                        break;
                    }
                }
                return foundChild;
            }
        }

        //Called when a mod is added, removed, or moved (not called when the mod list is loaded at the start of the program)
        private void ModList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            FindConflicts();
            UpdateConflictsFlags();
        }

        //Performs all the post steam query tasks
        private void OnModListLoaded()
        {
            //Loads saved mod order
            modList = new ObservableModList(modList.OrderBy(x => Array.FindIndex(Settings.CurrentProfile.OrderedModsNames, y => y == x.Name)));
            foreach (Mod mod in modList)
            {
                mod.Enabled = Settings.CurrentProfile.EnabledMods.Contains(mod.Name);
            }
            listViewMods.ItemsSource = modList; //Since the modList got re-instantiated, we need to give the listView the new reference
            FindConflicts();
            UpdateConflictsFlags();
            if (listViewMods.Items.Count > 0) listViewMods.SelectedIndex = 0;
        }

        //Finds mods conflicts
        private void FindConflicts()
        {
            List<List<string>> modsFilesCollection = new List<List<string>>();
            foreach (Mod mod in modList)
            {
                if (mod.PackFile == null) continue;
                List<string> modFiles = mod.PackFile.Files.Select(x => x.FullPath).ToList();
                for (int i = 0; i < modsFilesCollection.Count; i++)
                {
                    var filesWithConflicts = modFiles.Intersect(modsFilesCollection[i]);
                    if (filesWithConflicts.Count() > 0)
                    {
                        mod.Conflicts.AddConflicts(filesWithConflicts, modList[i].Name);
                        modList[i].Conflicts.AddConflicts(filesWithConflicts, mod.Name);
                    }
                }
                modsFilesCollection.Add(modFiles);
            }
        }

        //Sets the conflicts flags on the listview
        private void UpdateConflictsFlags()
        {
            foreach (Mod mod in modList)
            {
                mod.Flag = "";
                mod.FlagColor = Brushes.Green;
            }
            foreach (Mod mod in modList)
            {
                if (mod.Conflicts.Count > 0)
                {
                    foreach (Mod otherMod in modList)
                    {
                        foreach (string conflictingModName in mod.Conflicts.ConflictingMods())
                        {
                            if (otherMod.Name == conflictingModName)
                            {
                                if (listViewMods.Items.IndexOf(otherMod) < listViewMods.Items.IndexOf(mod))
                                {
                                    if (!otherMod.Flag.Contains("−"))
                                    {
                                        if (otherMod.Flag != "+") otherMod.FlagColor = Brushes.Red;
                                        otherMod.Flag += "−";
                                    }
                                }
                                else
                                {
                                    otherMod.FlagColor = Brushes.Green;
                                    if (!otherMod.Flag.Contains("+")) otherMod.Flag += "+";
                                    
                                }
                            }
                        }
                    }
                }
            }
        }

        //Called when the Steam vote query returns asynchronously
        private void OnSendVoteQueryUGCRequest(GetUserItemVoteResult_t param, bool bIOFailure)
        {
            if (param.m_eResult == EResult.k_EResultOK)
            {
                if (param.m_bVotedUp)
                {
                    modList[param.m_nPublishedFileId.m_PublishedFileId].Vote = true;
                }
                else if (param.m_bVotedDown)
                {
                    modList[param.m_nPublishedFileId.m_PublishedFileId].Vote = false;
                }
                else if (param.m_bVoteSkipped)
                {
                    modList[param.m_nPublishedFileId.m_PublishedFileId].Vote = null;
                }
            }
        }

        //When a new mod is selected, updates the info panel
        private void listViewMods_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (listViewMods.SelectedItem == null) return; //If a drag-drop operation is happening, returns;
            string selectedModName = ((Mod)listViewMods.SelectedItem).Name;
            Mod selectedMod = modList[selectedModName];
            //Mod name
            labelModName.Content = selectedMod.Name;
            //Mod image
            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(selectedMod.ImageUrl, UriKind.Absolute);
            bitmap.EndInit();
            imageModPicture.Source = bitmap;
            //Mod description
            labelDescription.Text = selectedMod.Description;
            //Mod file tree
            treeViewModFiles.Items.Clear();
            rangeBarTreeView.Children.Clear();
            if (selectedMod.PackFile != null) BuildTree(selectedMod.PackFile.Root, treeViewModFiles.Items);
            //Mod conflicts
            rangeBarMods.Children.Clear();
            if (selectedMod.Conflicts.Count > 0) AddMarkOnRangeBar(rangeBarMods, listViewMods.SelectedIndex * 100 / listViewMods.Items.Count, new BrushConverter().ConvertFromString("#FF3761F1") as Brush);
            foreach (Mod item in listViewMods.Items)
            {
                item.Overridden = item.Overrides = false;
                foreach (string modName in selectedMod.Conflicts.ConflictingMods())
                {
                    if (item.Name == modName)
                    {
                        int index = listViewMods.Items.IndexOf(item);
                        if (index > listViewMods.SelectedIndex)
                        {
                            item.Overridden = true;
                            AddMarkOnRangeBar(rangeBarMods, index * 100 / listViewMods.Items.Count, new BrushConverter().ConvertFromString("#FFFF7373") as Brush);
                        }
                        else
                        {
                            item.Overrides = true;
                            AddMarkOnRangeBar(rangeBarMods, index * 100 / listViewMods.Items.Count, new BrushConverter().ConvertFrom("#00e600") as Brush);
                        }
                    }
                }
            }

            //Internal method used to recursively populate the file tree
            void BuildTree(VirtualDirectory _virtualDirectory, ItemCollection _addInMe)
            {
                int _i = 0;
                RecursiveBuildTree(_virtualDirectory, _addInMe, ref _i);

                void RecursiveBuildTree(VirtualDirectory virtualDirectory, ItemCollection addInMe, ref int i) //i needs to be passed by reference so that every recoursive call increments the same counter
                {
                    TreeViewItem currentNode = new TreeViewItem();
                    currentNode.Header = virtualDirectory.Name;
                    currentNode.IsExpanded = true;
                    i++;
                    foreach (var file in virtualDirectory.Files)
                    {
                        TreeViewItem childNode = new TreeViewItem();
                        childNode.Header = file.Name;
                        if (selectedMod.Conflicts.AnyConflict(file.FullPath))
                        {
                            childNode.ToolTip = $"Conflicts with:{Environment.NewLine}  {String.Join(Environment.NewLine + "  ", selectedMod.Conflicts.ConflictingMods(file.FullPath))}";
                            double position = i * 100 / selectedMod.PackFile.Root.AllEntries.Count;
                            if (selectedMod.Conflicts.ConflictingMods(file.FullPath).ToList().TrueForAll(otherMod => modList.IndexOf(modList[otherMod]) < modList.IndexOf(selectedMod)))
                            {
                                childNode.Foreground = Brushes.Green;
                                AddMarkOnRangeBar(rangeBarTreeView, position, new BrushConverter().ConvertFrom("#00e600") as Brush);
                            }
                            else
                            {
                                childNode.Foreground = Brushes.Red;
                                AddMarkOnRangeBar(rangeBarTreeView, position, new BrushConverter().ConvertFromString("#FFFF7373") as Brush);
                            }
                        }
                        currentNode.Items.Add(childNode);
                        i++;
                    }
                    foreach (var subdir in virtualDirectory.Subdirectories)
                    {
                        RecursiveBuildTree(subdir, currentNode.Items, ref i);
                    }
                    addInMe.Add(currentNode);
                }
            }

            //Internal method used to add marks on the scrollbar where the conflicting mods are
            void AddMarkOnRangeBar(RangeBar rangeBar, double position, Brush color)
            {
                System.Windows.Shapes.Rectangle r = new System.Windows.Shapes.Rectangle
                {
                    Fill = color,
                    Height = 4,
                    IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                RangeBar.SetPosition(r, position);
                rangeBar.Children.Add(r);
            }
        }

        //Loads a new profile
        private void comboBoxProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (comboBoxProfile.SelectedIndex > 0)
            {
                if (loadingProfile) loadingProfile = false; //If it's loading settings from a profile, avoids overwriting the still-to-load settings
                else //Saves current settings before switching profile
                {
                    Settings.Profiles[Settings.CurrentProfileIndex].OrderedModsNames = modList.NameList;
                    Settings.Profiles[Settings.CurrentProfileIndex].EnabledMods = modList.EnabledModList;
                }
                Settings.ChangeCurrentProfile(comboBoxProfile.SelectedItem.ToString());
                radioButtonDx12.IsChecked = !(radioButtonDx11.IsChecked = Settings.CurrentProfile.DxType == "dx11");
                checkBoxQuickLaunch.IsChecked = Settings.CurrentProfile.QuickLaunch;
                OnModListLoaded();
            }
        }

        //Starts the game
        private void buttonPlay_Click(object sender, RoutedEventArgs e)
        {
            StartGame(false);
            Close();
        }

        //Starts the game loading the latest save
        private void buttonLoad_Click(object sender, RoutedEventArgs e)
        {
            StartGame(true);
            Close();
        }

        private void StartGame(bool loadLatestSave)
        {
            string enabled_mods = "";
            foreach (var mod in modList)
            {
                enabled_mods += $"add_working_directory \"{Path.GetDirectoryName(Path.GetFullPath(mod.FilePath))}\";mod \"{Path.GetFileName(mod.FilePath)}\";";
            }
            File.WriteAllText("enabled_mods.txt", enabled_mods);
            string arguments;
            if (loadLatestSave)
            {
                DirectoryInfo savesDirectory = new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"The Creative Assembly\Warhammer\save_games"));
                string latestSave = savesDirectory.GetFiles().OrderByDescending(f => f.LastWriteTime).First().Name;
                arguments = $"game_startup_mode campaign_load \"{latestSave}\";enabled_mods.txt; -{Settings.CurrentProfile.DxType}";
            }
            else arguments = $"enabled_mods.txt; -{Settings.CurrentProfile.DxType}";
            string exeName = File.Exists("Warhammer2.exe") ? "Warhammer2.exe" : "Warhammer.exe";
            ProcessStartInfo startInfo = new ProcessStartInfo(exeName, arguments);
            startInfo.WorkingDirectory = Environment.CurrentDirectory;
            Process.Start(startInfo);
        }

        //Saves settings and close the program
        private void MainWindow1_Closed(object sender, EventArgs e)
        {
            SteamAPI.Shutdown();
            if (modList.Count > 0)
            {
                Settings.Profiles[Settings.CurrentProfileIndex].OrderedModsNames = modList.NameList;
                Settings.Profiles[Settings.CurrentProfileIndex].EnabledMods = modList.EnabledModList;
            }
            Settings.SaveSettings("profiles.xml");
        }

        //Updates the values stored in the modList object 
        private void CheckBoxMod_CheckChanged(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = (CheckBox)sender;
            string modName = checkBox.Tag.ToString();
            modList[modName].Enabled = checkBox.IsChecked.Value;
            //If at least one mod is disabled, uncheck the "Enable All" checkbox
            if (checkBox.IsChecked == false)
            {
                checkBoxEnableAll.Unchecked -= checkBoxEnableAll_Unchecked;
                checkBoxEnableAll.IsChecked = false;
                checkBoxEnableAll.Unchecked += checkBoxEnableAll_Unchecked;
            }
            //If every mod is enabled, check the "Enable All" checkbox
            bool allChecked = true;
            foreach (Mod item in listViewMods.Items)
            {
                if (!item.Enabled) allChecked = false;
            }
            if (allChecked)
            {
                checkBoxEnableAll.Checked -= checkBoxEnableAll_Checked;
                checkBoxEnableAll.IsChecked = true;
                checkBoxEnableAll.Checked += checkBoxEnableAll_Checked;
            }
        }
        
        //Enables all the mods
        private void checkBoxEnableAll_Checked(object sender, RoutedEventArgs e)
        {
            foreach (Mod item in listViewMods.Items)
                modList[item.Name].Enabled = true;
        }

        //Disables all the mods
        private void checkBoxEnableAll_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (Mod item in listViewMods.Items)
                modList[item.Name].Enabled = false;
        }

        //Deletes a profile
        private void buttonRemoveProfile_Click(object sender, RoutedEventArgs e)
        {
            if (comboBoxProfile.Items.Count > 2) //The button "New profile..." counts as an elements; checks that at least one profile remains active
            {
                Button button = (Button)sender;
                string profileName = button.Tag.ToString();
                Settings.RemoveProfile(profileName);
                comboBoxProfile.Items.Remove(profileName);
                loadingProfile = true;
                comboBoxProfile.SelectedIndex = Settings.CurrentProfileIndex + 1;
            }
            else MessageBox.Show("You need to have at least one profile!", "Total War Mod Manager");
        }

        //Enables/disables start with Steam
        private void checkBoxStartWithSteam_Checked(object sender, RoutedEventArgs e)
        {
            if (checkBoxStartWithSteam.IsChecked == true)
            {
                checkBoxQuickLaunch.IsEnabled = true;
                if (new FileInfo("launcher\\launcher.exe").Length > 1000000) //CA launcher is currently used
                {
                    checkBoxStartWithSteam.IsEnabled = false;
                    File.Replace("launcher.exe", "launcher\\launcher.exe", "launcher\\launcher.exe.bak");
                    File.Copy("steam_appid.txt", "launcher\\steam_appid.txt");
                }
            }
            else
            {
                checkBoxQuickLaunch.IsEnabled = false;
                if (new FileInfo("launcher\\launcher.exe").Length < 1000000) //This launcher is currently used
                {
                    checkBoxStartWithSteam.IsEnabled = false;
                    File.WriteAllBytes("launcher\\launcher.exe", File.ReadAllBytes("launcher\\launcher.exe.bak"));
                    File.Delete("launcher\\launcher.exe.bak");
                    File.Delete("launcher\\steam_appid.txt");
                }
            }
            checkBoxStartWithSteam.IsEnabled = true;
        }

        //Enables/disables quick launch
        private void checkBoxQuickLaunch_Checked(object sender, RoutedEventArgs e)
        {
            Settings.Profiles[Settings.CurrentProfileIndex].QuickLaunch = checkBoxQuickLaunch.IsChecked.Value;
        }

        //Changes the DirectX version used by the game
        private void radioButtonDx11_Checked(object sender, RoutedEventArgs e)
        {
            Settings.Profiles[Settings.CurrentProfileIndex].DxType = radioButtonDx11.IsChecked.Value ? "dx11" : "dx12";
        }

        //Shows a windows to create a new profile
        private void buttonNewProfile_Click(object sender, RoutedEventArgs e)
        {
            List<string> profileList = new List<string>();
            foreach (var item in comboBoxProfile.Items)
            {
                profileList.Add(item.ToString());
            }
            WindowNewProfile window = new WindowNewProfile(profileList.Skip(1).ToArray());
            if (window.ShowDialog(out string profileName, out string copyFrom).Value)
            {
                Settings.CloneProfile(profileName, copyFrom);
                comboBoxProfile.Items.Add(profileName);
                comboBoxProfile.SelectedIndex = comboBoxProfile.Items.Count - 1;
                Settings.ChangeCurrentProfile(profileName);
            }
        }

        //Cancel the subscription to the selected workshop item
        private void buttonUnsubscribe_Click(object sender, RoutedEventArgs e)
        {
            SteamUGC.UnsubscribeItem(new PublishedFileId_t(modList[listViewMods.SelectedIndex].ID));
            int index = listViewMods.SelectedIndex;
            modList.RemoveAt(listViewMods.SelectedIndex);
            listViewMods.SelectedIndex = index <= (listViewMods.Items.Count - 1) ? index : (index - 1);
        }

        //Upvotes the selected mod
        private void buttonVoteUp_Click(object sender, RoutedEventArgs e)
        {
            SteamUGC.SetUserItemVote(new PublishedFileId_t(modList[listViewMods.SelectedIndex].ID), true);
            modList[listViewMods.SelectedIndex].Vote = true;
            (listViewMods.Items[listViewMods.SelectedIndex] as Mod).Vote = true;
        }

        //Opens the Steam Cloud Manager window
        private void buttonSteamCloud_Click(object sender, RoutedEventArgs e)
        {
            new SteamCloudManagerWindow(UInt32.Parse(File.ReadAllText("steam_appid.txt"))).ShowDialog();
        }

        //Opens the Preview window
        private void OnItemMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var clickedItem = TryGetClickedItem(treeViewModFiles, e);
            if (clickedItem == null)
                return;
            PackedFile packedFile = modList[listViewMods.SelectedIndex].PackFile.Files.Find(f => f.Name.Contains(clickedItem.Header.ToString()));
            if (packedFile == null) return;
            if (packedFile.Name.EndsWith(".png") || packedFile.Name.EndsWith(".dds"))
                new ImagePreview(packedFile).Show();
            else if (TextPreview.SUPPORTED_FORMATS.Contains(Path.GetExtension(packedFile.Name)))
                new TextPreview(packedFile).Show();

            TreeViewItem TryGetClickedItem(TreeView treeView, System.Windows.Input.MouseButtonEventArgs _e)
            {
                var hit = _e.OriginalSource as DependencyObject;
                while (hit != null && !(hit is TreeViewItem))
                    hit = VisualTreeHelper.GetParent(hit);
                return hit as TreeViewItem;
            }
        }

        //Downvotes the selected mod
        private void buttonVoteDown_Click(object sender, RoutedEventArgs e)
        {
            SteamUGC.SetUserItemVote(new PublishedFileId_t(modList[listViewMods.SelectedIndex].ID), false);
            modList[listViewMods.SelectedIndex].Vote = false;
            (listViewMods.Items[listViewMods.SelectedIndex] as Mod).Vote = false;
        }

        //Opens the mod page on the Steam Workshop
        private void buttonWorkshop_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new Uri($"http://steamcommunity.com/sharedfiles/filedetails/?id={modList[listViewMods.SelectedIndex].ID}").AbsoluteUri);
        }

    }


    public class ListViewModItemTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            FrameworkElement elemnt = container as FrameworkElement;
            Mod mod = item as Mod;
            //Choose the template based on the mod download state
            if (!mod.Downloading)
            {
                return elemnt.FindResource("modTemplate") as DataTemplate;
            }
            else
            {
                return elemnt.FindResource("modDownloadTemplate") as DataTemplate;
            }
        }
    }
}