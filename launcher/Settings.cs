using System;
using System.IO;
using System.Xml.Serialization;

namespace launcher
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
    }

    [Serializable]
    public struct Profile
    {
        public string Name { get; set; }
        public string DxType { get; set; }
        public string[] EnabledMods { get; set; }
        public bool LaunchWithSteam { get; set; }
        public bool QuickLaunch { get; set; }
        public bool LastUsed { get; set; }
    }
}