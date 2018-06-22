using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace Total_War_Mod_Manager
{
    static class Settings
    {
        public static Profile[] Profiles { get; set; }
        public static int CurrentProfileIndex { get; set; }
        public static Profile CurrentProfile
        {
            get
            {
                return Profiles[CurrentProfileIndex];
            }
        }

        public static void LoadSettings(string filePath)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(Profile[]));
            using (var fileStream = new FileStream(filePath, FileMode.Open))
            {
                Profiles = serializer.Deserialize(fileStream) as Profile[];
            }
            CurrentProfileIndex = Array.FindIndex(Profiles, x => x.LastUsed == true);
        }

        public static void SaveSettings(string filePath)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(Profile[]));
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                serializer.Serialize(fileStream, Profiles);
            }
        }

        public static void CreateDefault()
        {
            Profile p = new Profile()
            {
                Name = "Default",
                DxType = "dx11",
                OrderedModsNames = new string[0],
                EnabledMods = new string[0],
                QuickLaunch = false,
                LastUsed = true
            };
            Profiles = new Profile[] { p };
            CurrentProfileIndex = 0;
            SaveSettings("profiles.xml");
        }

        public static void CloneProfile(string profileName, string profileToClone)
        {
            Profile newProfile = Profiles.First(x => x.Name == profileToClone);
            newProfile.Name = profileName;
            var profilesList = Profiles.ToList();
            profilesList.Add(newProfile);
            Profiles = profilesList.ToArray();
        }

        public static void RemoveProfile(string profileName)
        {
            var profileList = Profiles.ToList();
            profileList.RemoveAll(x => x.Name == profileName);
            Profiles = profileList.ToArray();
            CurrentProfileIndex = 0;
        }

        public static void ChangeCurrentProfile(string profileName)
        {
            CurrentProfileIndex = Array.FindIndex(Profiles, x => x.Name == profileName);
            for (int i = 0; i < Profiles.Length; i++)
            {
                Profiles[i].LastUsed = i == CurrentProfileIndex;
            }
        }
    }

    [Serializable]
    public struct Profile
    {
        public string Name { get; set; }
        public string DxType { get; set; }
        public string[] OrderedModsNames { get; set; }
        public string[] EnabledMods { get; set; }
        public bool QuickLaunch { get; set; }
        public bool LastUsed { get; set; }
    }
}