using System;
using System.Collections.Generic;
using Steamworks;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Linq;

namespace launcher
{
    class Program
    {
        static CallResult<SteamUGCQueryCompleted_t> itemNameQueryResult;
        static List<string> modPackList = new List<string>();
        static string parentDirectory;

        static void Main(string[] args)
        {
            parentDirectory = new DirectoryInfo(Environment.CurrentDirectory).Parent.FullName.TrimEnd('\\');
            Settings.LoadSettings(parentDirectory + "\\profiles.xml");
            if (Settings.CurrentProfile.QuickLaunch)
            {
                if (!SteamAPI.Init())
                {
                    MessageBox.Show("Steam must be running");
                }
                else
                {
                    uint subItemCount = SteamUGC.GetNumSubscribedItems();
                    PublishedFileId_t[] subItemList = new PublishedFileId_t[subItemCount];
                    SteamUGC.GetSubscribedItems(subItemList, subItemCount);
                    foreach (var item in subItemList)
                    {
                        PublishedFileId_t[] itemArray = new PublishedFileId_t[1] { item };
                        UGCQueryHandle_t itemQuery = SteamUGC.CreateQueryUGCDetailsRequest(itemArray, 1);
                        SteamAPICall_t itemCall = SteamUGC.SendQueryUGCRequest(itemQuery);
                        itemNameQueryResult = CallResult<SteamUGCQueryCompleted_t>.Create(OnSendQueryUGCRequest);
                        itemNameQueryResult.Set(itemCall, OnSendQueryUGCRequest);
                    }
                    while (true)
                    {
                        SteamAPI.RunCallbacks();
                        if (modPackList.Count == subItemCount)
                        {
                            break;
                        }
                        Thread.Sleep(50);
                    }
                    string arguments = $"mod {string.Join("; mod ", modPackList)}; -{Settings.CurrentProfile.DxType}";
                    ProcessStartInfo psi = new ProcessStartInfo(parentDirectory + "\\Warhammer.exe", arguments);
                    psi.WorkingDirectory = parentDirectory;
                    Process.Start(psi);
                }
            }
            else
            {
                ProcessStartInfo psi = new ProcessStartInfo(parentDirectory + "\\Total War Mod Manager.exe");
                psi.WorkingDirectory = parentDirectory;
                Process.Start(psi);
            }
        }

        //Called when the Steam query returns asynchronously
        private static void OnSendQueryUGCRequest(SteamUGCQueryCompleted_t param, bool bIOFailure)
        {
            if (param.m_eResult == EResult.k_EResultOK)
            {
                UGCQueryHandle_t UGCHandle = param.m_handle;
                SteamUGC.GetQueryUGCResult(UGCHandle, 0, out SteamUGCDetails_t itemDetails);
                if (Settings.CurrentProfile.EnabledMods.Contains(itemDetails.m_rgchTitle)) modPackList.Add(itemDetails.m_pchFileName.Remove(0, 5));
            }
        }
    }
}