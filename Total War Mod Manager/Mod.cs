using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Total_War_Mod_Manager
{
    //Class that defines a Steam Workshop item
    public class Mod : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        //Binding targets
        private bool enabled;
        public bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Enabled"));
            }
        }
        public string Name { get; set; }
        private string flag;
        public string Flag
        {
            get { return flag; }
            set
            {
                flag = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Flag"));
            }
        }
        private bool overrides;
        public bool Overrides
        {
            get { return overrides; }
            set
            {
                overrides = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Overrides"));
            }
        }
        private bool overridden;
        public bool Overridden
        {
            get { return overridden; }
            set
            {
                overridden = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Overridden"));
            }
        }
        private Brush flagColor;
        public Brush FlagColor
        {
            get { return flagColor; }
            set
            {
                flagColor = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("FlagColor"));
            }
        }
        public Visibility VoteUpVisible
        {
            get { return (vote ?? false) ? Visibility.Visible : Visibility.Hidden; }
        }
        public Visibility VoteDownVisible
        {
            get { return (!vote ?? false) ? Visibility.Visible : Visibility.Hidden; }
        }
        //Other properties
        public ulong ID { get; set; }
        public string FilePath { get; set; }
        public string ImageUrl { get; set; }
        private bool? vote;
        public bool? Vote
        {
            get { return vote; }
            set
            {
                vote = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("VoteDownVisible"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("VoteUpVisible"));
            }
        }
        private string description;
        public string Description
        {
            get { return description; }
            //Makes the description more readable
            set
            {
                int lastIndexOf = value.LastIndexOfAny(new char[] { '.', '!', '?', ';' });
                if (lastIndexOf > -1) description = RemoveHtmlTags(value.Remove(lastIndexOf));
            }
        }
        public ConflictsCollection Conflicts { get; }
        private PackFile packFile;
        public PackFile PackFile
        {
            get
            {
                return packFile ?? (packFile = File.Exists(FilePath) ? pfc.Open(FilePath) : null);
            }
        }
        private PackFileCodec pfc;
        public bool Downloading { get; set; }
        private int progress;
        public int Progress
        {
            get { return progress; }
            set
            {
                progress = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Progress"));
            }
        }

        public Mod(ulong itemID, string name, string packName)
        {
            ID = itemID;
            Name = name;
            FilePath = packName;
            Conflicts = new ConflictsCollection();
            pfc = new PackFileCodec();
        }

        private string RemoveHtmlTags(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '[')
                {
                    int count = text.IndexOf(']', i) + 1 - i;
                    text = text.Remove(i, count < 0 ? text.Length - i : count);
                    i -= 1;
                }
            }
            return text;
        }
    }


    public class ConflictsCollection : List<ConflictsCollection.Conflict>
    {
        public struct Conflict
        {
            public Conflict(string filePath, string conflictingMod)
            {
                this.filePath = filePath;
                this.conflictingMod = conflictingMod;
            }

            public string filePath;
            public string conflictingMod;
        }

        public void AddConflicts(IEnumerable<string> fileNames, string modName)
        {
            AddRange(fileNames.Select(x => new Conflict(x, modName)));
        }

        //Returns a list of all the mods that this mod is conflicting with
        public string[] ConflictingMods()
        {
            return this.Select(x => x.conflictingMod).Distinct().ToArray();
        }

        //Returns a list of all the mods that this mod is conflicting with, with a specific file
        public string[] ConflictingMods(string filePath)
        {
            return this.Where(x => x.filePath == filePath).Select(x => x.conflictingMod).Distinct().ToArray();
        }

        //Returns if this mod has any conflicts with a specific file
        public bool AnyConflict(string filePath)
        {
            return this.Any(x => x.filePath == filePath);
        }
    }


    public class ObservableModList : ObservableCollection<Mod>
    {
        public Mod this[string name]
        {
            get { return this.Where(x => x.Name == name).FirstOrDefault(); }
            set { base[IndexOf(this.Where(x => x.Name == name).FirstOrDefault())] = value; }
        }
        public Mod this[ulong itemID]
        {
            get { return this.Where(x => x.ID == itemID).FirstOrDefault(); }
            set { base[IndexOf(this.Where(x => x.ID == itemID).FirstOrDefault())] = value; }
        }
        public string[] PackFileList
        {
            get { return this.Select(x => x.FilePath).ToArray(); }
        }
        public string[] NameList
        {
            get { return this.Select(x => x.Name).ToArray(); }
        }
        public string[] EnabledModList
        {
            get { return this.Where(x => x.Enabled == true).Select(x => x.Name).ToArray(); }
        }

        public ObservableModList()
        {
        }

        public ObservableModList(IEnumerable<Mod> list)
        {
            foreach (var item in list)
            {
                Add(item);
            }
        }

        public bool Contains(ulong itemID)
        {
            foreach (var item in this)
            {
                if (item.ID == itemID) return true;
            }
            return false;
        }
    }
}