using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Total_War_Mod_Manager
{
    /*
     * A Pack file containing data in form of PackedFile entries from the Warscape engine.
     */
    [DebuggerDisplay("Filepath = {Filepath}")]
    public class PackFile : IEnumerable<PackedFile>
    {
        public delegate void ModifyEvent();
        public event ModifyEvent Modified;

        private PFHeader header;
        private bool modified;

        #region Attributes
        // header access
        public PFHeader Header
        {
            get { return header; }
        }

        // the path on the file system
        public string Filepath
        {
            get;
            private set;
        }

        // the root node of this file;
        // named with the file name, stripped from any FullPath query of entries
        public VirtualDirectory Root
        {
            get;
            private set;
        }

        // Query type from header; calls Modified when set
        public PackType Type
        {
            get { return Header.Type; }
            set
            {
                if (value != Header.Type)
                {
                    Header.Type = value;
                    IsModified = true;
                }
            }
        }
        public bool IsShader
        {
            get { return Header.IsShader; }
            set
            {
                if (value != Header.IsShader)
                {
                    Header.IsShader = value;
                    IsModified = true;
                }
            }
        }
        // Modified attribute, calls Modified event after set
        public bool IsModified
        {
            get { return modified; }
            set
            {
                modified = value;
                if (Modified != null)
                {
                    Modified();
                }
            }
        }
        #endregion

        /*
         * Create pack file at the given path with the given header.
         */
        public PackFile(string path, PFHeader h)
        {
            header = h;
            Filepath = path;
            Root = new VirtualDirectory() { Name = Path.GetFileName(path) };
            DirAdded(Root);
        }
        /*
         * Create PackFile at the given path with a default header of type Mod and type PFH3.
         */
        public PackFile(string path) : this(path, new PFHeader("PFH3")
        {
            Type = PackType.Mod
        })
        { }
        /*
         * Add the given file to this pack.
         */
        public void Add(PackedFile file, bool replace = false)
        {
            Root.Add(file.FullPath, file, replace);
        }

        #region Entry Access
        // lists all contained packed files
        public List<PackedFile> Files
        {
            get
            {
                return Root.AllFiles;
            }
        }
        // retrieves the packed file at the given path name
        public PackEntry this[string filepath]
        {
            get
            {
                string[] paths = filepath.Split(Path.DirectorySeparatorChar);
                VirtualDirectory dir = Root;
                PackEntry result = dir;
                foreach (string subDir in paths)
                {
                    result = dir.GetEntry(subDir);
                    dir = result as VirtualDirectory;
                }
                return result;
            }
        }
        public int FileCount
        {
            get
            {
                return Root.AllFiles.Count;
            }
        }
        #endregion

        public override string ToString()
        {
            return string.Format("Pack file {0}", Filepath);
        }

        #region Event Handler for Entries
        // Set self to modified
        private void EntryModified(PackEntry file)
        {
            IsModified = true;
        }
        // Set modified
        private void EntryRenamed(PackEntry file, string name)
        {
            EntryModified(file);
        }
        // Register modified and rename handlers
        private void EntryAdded(PackEntry file)
        {
            file.ModifiedEvent += EntryModified;
            file.RenameEvent += EntryRenamed;
            IsModified = true;
        }
        // Unregister modified and rename handlers
        private void EntryRemoved(PackEntry entry)
        {
            entry.ModifiedEvent -= EntryModified;
            entry.RenameEvent -= EntryRenamed;
        }
        // Call EntryAdded and register Added and Removed handlers
        private void DirAdded(PackEntry dir)
        {
            EntryAdded(dir);
            (dir as VirtualDirectory).FileAdded += EntryAdded;
            (dir as VirtualDirectory).DirectoryAdded += DirAdded;
            (dir as VirtualDirectory).FileRemoved += EntryRemoved;
        }
        #endregion

        public IEnumerator<PackedFile> GetEnumerator()
        {
            return Files.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }


    /*
     * Class containing general pack file information.
     */
    public class PFHeader
    {
        string identifier;

        public PFHeader(string id)
        {
            PrecedenceByte = 3;
            // headers starting from Rome II are longer
            switch (id)
            {
                case "PFH4":
                case "PFH5":
                    DataStart = 0x28;
                    break;
                default:
                    DataStart = 0x20;
                    break;
            }
            PackIdentifier = id;
            FileCount = 0;
            Version = 0;
            ReplacedPackFileNames = new List<string>();
        }

        /*
         * Create a header from the given one.
         */
        public PFHeader(PFHeader toCopy) : this(toCopy.identifier)
        {
            Type = toCopy.Type;
            ReplacedPackFileNames.AddRange(toCopy.ReplacedPackFileNames);
        }

        /*
         * The lenght in bytes of the entry containing the filenames
         * replaced by this pack file.
         */
        public int ReplacedFileNamesLength
        {
            get
            {
                // start with 0 byte for each name
                int result = ReplacedPackFileNames.Count;
                // add actual names' lengths
                ReplacedPackFileNames.ForEach(name => result += name.Length);
                return result;
            }
        }

        // query/set identifier
        // throws Exception if unknown
        public string PackIdentifier
        {
            get
            {
                return identifier;
            }
            set
            {
                switch (value)
                {
                    case "PFH0":
                    case "PFH2":
                    case "PFH3":
                    case "PFH4":
                    case "PFH5":
                        break;
                    default:
                        throw new Exception("Unknown Header Type " + value);
                }
                identifier = value;
            }
        }
        // query/set pack type
        private byte precedenceByte;
        public PackType Type
        {
            get
            {
                // filter three lsbs
                byte typeBits = (byte)LoadOrder;
                if (typeBits < 5)
                {
                    return (PackType)typeBits;
                }
                else
                {
                    return PackType.Other;
                }
            }
            set
            {
                // avoid setting invalid value
                int typeBits = (int)value & 7;
                // remove 3 lsbs from precedence
                precedenceByte &= byte.MaxValue - 7;
                // set bits
                precedenceByte |= (byte)typeBits;
            }
        }
        public byte PrecedenceByte
        {
            get
            {
                return precedenceByte;
            }
            set
            {
                precedenceByte = value;
            }
        }
        public bool IsShader
        {
            get
            {
                return (precedenceByte & 0x40) != 0;
            }
            set
            {
                if (value)
                {
                    precedenceByte |= 0x40;
                }
                else
                {
                    precedenceByte = (byte)(precedenceByte & ~0x40);
                }
            }
        }
        public int LoadOrder
        {
            get
            {
                return precedenceByte & 7;
            }
        }
        public bool HasAdditionalInfo
        {
            get
            {
                // bit 1000000 set?
                return IsShader;
            }
        }
        // query/set version
        public int Version { get; set; }
        // query/set offset for data in file
        public long DataStart { get; set; }
        // query/set number of contained files
        public UInt32 FileCount { get; set; }
        // query/set names of pack file replaced by this
        public UInt32 Unknown { get; set; }
        public List<string> ReplacedPackFileNames
        {
            get;
            set;
        }
        // query length of header itself
        public int Length
        {
            get
            {
                int result;
                switch (PackIdentifier)
                {
                    case "PFH0":
                        result = 0x18;
                        break;
                    case "PFH2":
                    case "PFH3":
                        // PFH2+ contain a FileTime at 0x1C (I think) in addition to PFH0's header
                        result = 0x20;
                        break;
                    case "PFH5":
                    case "PFH4":
                        result = 0x1C;
                        break;
                    default:
                        // if this ever happens, go have a word with MS
                        throw new Exception("Unknown header ID " + PackIdentifier);
                }
                return result;
            }
        }
        public long AdditionalInfo
        {
            get; set;
        }
    }


    /*
     * Types of pack files.
     */
    public enum PackType
    {
        // up to movie, ids are sequential
        Boot,    // 000
        Release, // 001
        Patch,   // 010
        Mod,     // 011
        Movie,   // 100
        Other
        /* ,
        // have to force id value for boot; there are more of those special ones,
        // but we can't handle them yet
        Sound = 17,
        Music = 18,
        Sound1 = 0x17,
        Music1 = 0x18,
        BootX = 0x40,
        Shader1 = 0x41,
        Shader2 = 0x42
        */
    }


    /*
     * Reads and writes Pack files from and to filesystem files.
     * I guess we could generalize to streams, but not much point to that for now.
     */
    public class PackFileCodec
    {
        public delegate void HeaderLoadedEvent(PFHeader header);
        public delegate void PackedFileLoadedEvent(PackedFile packed);
        public delegate void PackFileLoadedEvent(PackFile pack);

        public event HeaderLoadedEvent HeaderLoaded;
        public event PackedFileLoadedEvent PackedFileLoaded;
        public event PackFileLoadedEvent PackFileLoaded;

        /*
         * Decode pack file at the given path.
         */
        public PackFile Open(string packFullPath)
        {
            PackFile file;
            long sizes = 0;
            using (var reader = new BinaryReader(new FileStream(packFullPath, FileMode.Open), Encoding.ASCII))
            {
                PFHeader header = ReadHeader(reader);
                file = new PackFile(packFullPath, header);
                OnHeaderLoaded(header);

                long offset = file.Header.DataStart;
                for (int i = 0; i < file.Header.FileCount; i++)
                {
                    uint size = reader.ReadUInt32();
                    sizes += size;
                    if (file.Header.HasAdditionalInfo)
                    {
                        header.AdditionalInfo = reader.ReadInt64();
                    }
                    if (file.Header.PackIdentifier == "PFH5")
                    {
                        reader.ReadByte();
                    }
                    string packedFileName = IOFunctions.ReadZeroTerminatedAscii(reader);
                    // this is easier because we can use the Path methods
                    // under both Windows and Unix
                    packedFileName = packedFileName.Replace('\\', Path.DirectorySeparatorChar);

                    PackedFile packed = new PackedFile(file.Filepath, packedFileName, offset, size);
                    file.Add(packed);
                    offset += size;
                    this.OnPackedFileLoaded(packed);
                }
            }
            this.OnFinishedLoading(file);
            file.IsModified = false;
            return file;
        }
        /*
         * Reads pack header from the given file.
         */
        public static PFHeader ReadHeader(string filename)
        {
            using (var reader = new BinaryReader(File.OpenRead(filename)))
            {
                return ReadHeader(reader);
            }
        }
        /*
         * Reads pack header from the given reader.
         */
        public static PFHeader ReadHeader(BinaryReader reader)
        {
            PFHeader header;
            string packIdentifier = new string(reader.ReadChars(4));
            header = new PFHeader(packIdentifier);
            int packType = reader.ReadInt32();
            header.PrecedenceByte = (byte)packType;
            // header.Type = (PackType)packType;
            header.Version = reader.ReadInt32();
            int replacedPackFilenameLength = reader.ReadInt32();
            reader.BaseStream.Seek(0x10L, SeekOrigin.Begin);
            header.FileCount = reader.ReadUInt32();
            UInt32 indexSize = reader.ReadUInt32();
            header.DataStart = header.Length + indexSize;

            if (header.PackIdentifier == "PFH4" || header.PackIdentifier == "PFH5")
            {
                header.Unknown = reader.ReadUInt32();
            }

            // go to correct position
            reader.BaseStream.Seek(header.Length, SeekOrigin.Begin);
            for (int i = 0; i < header.Version; i++)
            {
                header.ReplacedPackFileNames.Add(IOFunctions.ReadZeroTerminatedAscii(reader));
            }
            header.DataStart += replacedPackFilenameLength;
            return header;
        }
        /*
         * Encodes given pack file to given path.
         */
        public void WriteToFile(string FullPath, PackFile packFile)
        {
            using (BinaryWriter writer = new BinaryWriter(new FileStream(FullPath, FileMode.Create), Encoding.ASCII))
            {
                writer.Write(packFile.Header.PackIdentifier.ToCharArray());
                writer.Write((int)packFile.Header.PrecedenceByte);
                writer.Write((int)packFile.Header.Version);
                writer.Write(packFile.Header.ReplacedFileNamesLength);
                UInt32 indexSize = 0;
                List<PackedFile> toWrite = new List<PackedFile>((int)packFile.Header.FileCount);
                foreach (PackedFile file in packFile.Files)
                {
                    if (!file.Deleted)
                    {
                        indexSize += (uint)file.FullPath.Length + 5;
                        if (packFile.Header.PackIdentifier == "PFH5")
                        {
                            indexSize += 1;
                        }
                        if (packFile.Header.HasAdditionalInfo)
                        {
                            indexSize += 8;
                        }
                        toWrite.Add(file);
                    }
                }
                writer.Write(toWrite.Count);
                writer.Write(indexSize);

                // File Time
                if (packFile.Header.PackIdentifier == "PFH2" || packFile.Header.PackIdentifier == "PFH3")
                {
                    Int64 fileTime = DateTime.Now.ToFileTimeUtc();
                    writer.Write(fileTime);
                }
                else if (packFile.Header.PackIdentifier == "PFH4" || packFile.Header.PackIdentifier == "PFH5")
                {
                    // hmmm
                    writer.Write(packFile.Header.Unknown);
                }

                // Write File Names stored from opening the file
                foreach (string replacedPack in packFile.Header.ReplacedPackFileNames)
                {
                    writer.Write(replacedPack.ToCharArray());
                    writer.Write((byte)0);
                }

                // pack entries are stored alphabetically in pack files
                toWrite.Sort(new PackedFileNameComparer());

                // write file list
                string separatorString = "" + Path.DirectorySeparatorChar;
                foreach (PackedFile file in toWrite)
                {
                    writer.Write((int)file.Size);
                    if (packFile.Header.HasAdditionalInfo)
                    {
                        writer.Write(packFile.Header.AdditionalInfo);
                    }
                    // pack pathes use backslash, we replaced when reading
                    string packPath = file.FullPath.Replace(separatorString, "\\");
                    if (packFile.Header.PackIdentifier == "PFH5")
                    {
                        writer.Write((byte)0);
                    }
                    writer.Write(packPath.ToCharArray());
                    writer.Write('\0');
                }
                foreach (PackedFile file in toWrite)
                {
                    if (file.Size > 0)
                    {
                        byte[] bytes = file.Data;
                        writer.Write(bytes);
                    }
                }
            }
        }

        /*
         * Save the given pack file to its current path.
         * Because some of its entries might still be drawing their data from the original pack,
         * we cannot just write over it.
         * Create a temp file, write into that, then delete the original and move the temp.
         */
        public void Save(PackFile packFile)
        {
            string tempFile = Path.GetTempFileName();
            WriteToFile(tempFile, packFile);
            if (File.Exists(packFile.Filepath))
            {
                File.Delete(packFile.Filepath);
            }
            File.Move(tempFile, packFile.Filepath);
        }
        /*
         * Notify pack header having been decoded.
         */
        private void OnHeaderLoaded(PFHeader header)
        {
            if (this.HeaderLoaded != null)
            {
                this.HeaderLoaded(header);
            }
        }
        /*
         * Notify pack fully decoded.
         */
        private void OnFinishedLoading(PackFile pack)
        {
            if (this.PackFileLoaded != null)
            {
                this.PackFileLoaded(pack);
            }
        }
        /*
         * Notify single pack file having been loaded.
         */
        private void OnPackedFileLoaded(PackedFile packed)
        {
            if (this.PackedFileLoaded != null)
            {
                this.PackedFileLoaded(packed);
            }
        }
    }

    /*
     * Compares two PackedFiles by name.
     */
    class PackedFileNameComparer : IComparer<PackedFile>
    {
        public int Compare(PackedFile a, PackedFile b)
        {
            return a.FullPath.CompareTo(b.FullPath);
        }
    }


    /*
     * Any entry in the Pack file.
     * Has a name and a path designating its full position.
     */
    public abstract class PackEntry : IComparable<PackEntry>
    {
        // Event triggered when name is about to be changed (called before actual change)
        public delegate void Renamed(PackEntry dir, String newName);
        public event Renamed RenameEvent;

        // Event triggered if any modification occurred on this entry
        // (called before modification is committed)
        public delegate void Modification(PackEntry file);
        public event Modification ModifiedEvent;

        // This Entry's Parent Entry
        public PackEntry Parent { get; set; }

        // Name; calls RenameEvent if changed
        string name;
        public string Name
        {
            get
            {
                return name;
            }
            set
            {
                if (RenameEvent != null)
                {
                    RenameEvent(this, value);
                }
                name = value;
            }
        }

        // Path
        public virtual string FullPath
        {
            get
            {
                string result = Name;
                PackEntry p = Parent;
                while (p != null)
                {
                    result = p.Name + Path.DirectorySeparatorChar + result;
                    p = p.Parent;
                }
                int index = result.IndexOf(Path.DirectorySeparatorChar);
                if (index != -1)
                {
                    result = result.Substring(index + 1);
                }
                return result;
            }
        }

        // Tag whether entry is tagged for deletion
        private bool deleted = false;
        public virtual bool Deleted
        {
            get
            {
                return deleted;
            }
            set
            {
                deleted = value;
                Modified = true;
            }
        }

        // Tag whether entry has been modified
        bool modified;
        public bool Modified
        {
            get { return modified; }
            set
            {
                if (modified != value)
                {
                    modified = value;
                    if (ModifiedEvent != null)
                    {
                        ModifiedEvent(this);
                    }
                    if (Parent != null)
                    {
                        Parent.Modified = value;
                    }
                }
            }
        }

        // Compare by entry names
        public int CompareTo(PackEntry entry)
        {
            return entry != null ? Name.CompareTo(entry.Name) : 0;
        }
    }


    /*
     * A pack entry containing actual data.
     * Data is provided by a DataSource object.
     */
    [DebuggerDisplay("{Name}")]
    public class PackedFile : PackEntry
    {
        public DateTime EditTime
        {
            get;
            set;
        }

        private static readonly byte[] EMPTY = new byte[0];

        // a PackedFile can exist alone, without its parent
        string fullPath;
        public override string FullPath
        {
            get
            {
                if (Parent != null)
                {
                    return base.FullPath;
                }
                else if (fullPath != null)
                {
                    return fullPath;
                }
                else
                {
                    return Name;
                }
            }
        }

        #region File Data access
        // retrieve the amount of available data
        public long Size
        {
            get { return Source.Size; }
        }
        // Retrieve the data from this object's source
        public byte[] Data
        {
            get
            {
                return Source == null ? EMPTY : Source.ReadData();
            }
            set
            {
                Source = new MemorySource(value);
                Modified = true;
                EditTime = DateTime.Now;
            }
        }
        // the data source object itself
        DataSource source;

        public DataSource Source
        {
            get { return source; }
            set
            {
                source = value;
                Modified = true;
                EditTime = DateTime.Now;
            }
        }
        #endregion

        #region Constructors
        public PackedFile() { }
        public PackedFile(string filename, bool fileSource = true)
        {
            fullPath = filename;
            Name = Path.GetFileName(filename);
            if (fileSource)
            {
                Source = new FileSystemSource(filename);
            }
            else
            {
                Source = new MemorySource(new byte[0]);
            }
            Modified = false;
            EditTime = File.GetLastWriteTime(filename);
        }
        public PackedFile(string packFile, string packedName, long offset, long len)
        {
            fullPath = packedName;
            Name = Path.GetFileName(packedName);
            Source = new PackedFileSource(packFile, offset, len);
            Modified = false;
            EditTime = File.GetLastWriteTime(packFile);
        }
        #endregion
    }


    /*
     * A pack file entry that can contain other entries.
     * Provides additional events for content changes.
     * Note that entries are usually not removed from this; instead they are tagged
     * with "Deleted" and just not added anymore when the full model is rebuilt
     * the next time around.
     */
    public class VirtualDirectory : PackEntry, IEnumerable<PackedFile>
    {
        public delegate void ContentsEvent(PackEntry entry);
        // triggered when content is added
        public event ContentsEvent DirectoryAdded;
        public event ContentsEvent FileAdded;
        // triggered when file is removed
        public event ContentsEvent FileRemoved;

        // override deletion to tag all contained objects as deleted as well
        public override bool Deleted
        {
            get
            {
                return base.Deleted;
            }
            set
            {
                if (Deleted != value)
                {
                    base.Deleted = value;
                    AllEntries.ForEach(e => e.Deleted = value);
                }
            }
        }

        #region Contained entry access
        // the contained directories
        private SortedSet<VirtualDirectory> subdirectories = new SortedSet<VirtualDirectory>();
        public SortedSet<VirtualDirectory> Subdirectories
        {
            get
            {
                return subdirectories;
            }
        }

        // the contained files
        private SortedSet<PackedFile> containedFiles = new SortedSet<PackedFile>();
        public SortedSet<PackedFile> Files
        {
            get
            {
                return containedFiles;
            }
        }

        // retrieve all files contained in this and all subdirectories
        public List<PackedFile> AllFiles
        {
            get
            {
                List<PackedFile> files = new List<PackedFile>();
                foreach (VirtualDirectory subDirectory in Subdirectories)
                {
                    files.AddRange(subDirectory.AllFiles);
                }
                files.AddRange(Files);
                return files;
            }
        }
        public List<PackEntry> AllEntries
        {
            get
            {
                List<PackEntry> result = new List<PackEntry>();
                result.Add(this);
                foreach (VirtualDirectory directory in Subdirectories)
                {
                    result.AddRange(directory.AllEntries);
                }
                result.AddRange(Files);
                return result;
            }
        }

        // enumerates all files
        public IEnumerator<PackedFile> GetEnumerator()
        {
            return AllFiles.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /*
         * Retrieve a list with all contained entries (files and directories).
         */
        public List<PackEntry> Entries
        {
            get
            {
                List<PackEntry> entries = new List<PackEntry>();
                entries.AddRange(containedFiles);
                entries.AddRange(subdirectories);
                return entries;
            }
        }

        /*
         * Retrieve the directory with the given name.
         * Will create and add an empty one if it doesn't already exists.
         */
        public VirtualDirectory GetSubdirectory(string subDir)
        {
            VirtualDirectory result = null;
            foreach (VirtualDirectory dir in subdirectories)
            {
                if (dir.Name.Equals(subDir))
                {
                    result = dir;
                    break;
                }
            }
            if (result == null)
            {
                result = new VirtualDirectory { Parent = this, Name = subDir };
                Add(result);
            }
            return result;
        }

        /*
         * Retrieve the contained entry with the given name.
         * Will return null if entry does not exist.
         */
        public PackEntry GetEntry(string name)
        {
            PackEntry result = null;
            foreach (PackEntry entry in Entries)
            {
                if (entry.Name.Equals(name))
                {
                    result = entry;
                    break;
                }
            }
            return result;
        }
        #endregion

        #region Add entries
        /*
         * Add the given directory to this.
         * Will notify the DirectoryAdded event after adding.
         */
        public void Add(VirtualDirectory dir)
        {
            subdirectories.Add(dir);
            dir.Parent = this;
            if (DirectoryAdded != null)
            {
                DirectoryAdded(dir);
            }
        }
        /*
         * Add the given content file.
         * Will notify the FileAdded event after adding.
         * If a file with the given name already exists and is deleted,
         * it is replaced with the given one.
         * If a file with the given name already exists and is not deleted,
         * it will not be replaced unless the "overwrite" parameter is set to true.
         */
        public void Add(PackedFile file, bool overwrite = false)
        {
            if (containedFiles.Contains(file))
            {
                PackedFile contained = null;
                foreach (PackedFile f in containedFiles)
                {
                    if (f.Name.Equals(file.Name))
                    {
                        contained = f;
                        break;
                    }
                }
                if (contained.Deleted || overwrite)
                {
                    containedFiles.Remove(contained);
                    if (FileRemoved != null)
                    {
                        FileRemoved(contained);
                    }
                }
                else
                {
                    // don't add the file
                    return;
                }
            }
            containedFiles.Add(file);
            file.Parent = this;
            if (FileAdded != null)
            {
                FileAdded(file);
            }
        }

        /*
         * Adds all file from the given directory path.
         */
        public void Add(string basePath)
        {
            string[] files = Directory.GetFiles(basePath, "*.*", SearchOption.AllDirectories);
            foreach (string filepath in files)
            {
                string relativePath = filepath.Replace(Path.GetDirectoryName(basePath), "");
                Add(relativePath, new PackedFile(filepath));
            }
        }
        /*
         * Adds the given file to the given path, relative to this directory.
         */
        public void Add(string relativePath, PackedFile file, bool overwrite = false)
        {
            char[] splitAt = { Path.DirectorySeparatorChar };
            string baseDir = Path.GetDirectoryName(relativePath);
            string[] dirs = baseDir != null ? baseDir.Split(splitAt, StringSplitOptions.RemoveEmptyEntries) : new string[0];
            VirtualDirectory current = this;
            if (dirs.Length > 0)
            {
                foreach (string dir in dirs)
                {
                    current = current.GetSubdirectory(dir);
                }
            }
            file.Parent = current;
            current.Add(file, overwrite);
        }
        #endregion
    }


    #region Data Sources
    /*
     * A class providing data for a PackedFile content object.
     */
    public abstract class DataSource
    {
        /*
         * Retrieve the amount of data in this source in bytes.
         * Has an attribute of its own to avoid having to call ReadData().length
         * every time which might be time-consuming.
         */
        public long Size
        {
            get;
            protected set;
        }
        public abstract byte[] ReadData();
    }

    /* Provides data from the local filesystem */
    [DebuggerDisplay("From file {filepath}")]
    class FileSystemSource : DataSource
    {
        protected string filepath;
        public FileSystemSource(string filepath)
            : base()
        {
            Size = new FileInfo(filepath).Length;
            this.filepath = filepath;
        }
        public override byte[] ReadData()
        {
            return File.ReadAllBytes(filepath);
        }
    }

    /* Provides data from heap memory */
    [DebuggerDisplay("From Memory")]
    class MemorySource : DataSource
    {
        private byte[] data;
        public MemorySource(byte[] data)
        {
            Size = data.Length;
            this.data = data;
        }
        public override byte[] ReadData()
        {
            return data;
        }
    }

    /* Provides data from within a pack file */
    [DebuggerDisplay("{Offset}@{filepath}")]
    class PackedFileSource : DataSource
    {
        private string filepath;
        public long Offset
        {
            get;
            private set;
        }
        public PackedFileSource(string packfilePath, long offset, long length)
        {
            Offset = offset;
            filepath = packfilePath;
            Size = length;
        }
        public override byte[] ReadData()
        {
            byte[] data = new byte[Size];
            using (Stream stream = File.OpenRead(filepath))
            {
                stream.Seek(Offset, SeekOrigin.Begin);
                stream.Read(data, 0, data.Length);
            }
            return data;
        }
    }
    #endregion


    /*
     * Utility methods to read common data from streams.
     */
    class IOFunctions
    {
        // filter string for tsv files
        public static string TSV_FILTER = "TSV Files (*.csv,*.tsv)|*.csv;*.tsv|Text Files (*.txt)|*.txt|All Files|*.*";
        // filter string for pack files
        public static string PACKAGE_FILTER = "Package File (*.pack)|*.pack|Any File|*.*";

        /*
         * Read a unicode string from the given reader.
         */
        public static string ReadCAString(BinaryReader reader)
        {
            return ReadCAString(reader, Encoding.Unicode);
        }
        /*
         * Read a string from the given reader, using the given encoding.
         * First 2 bytes contain the string length, string is not zero-terminated.
         */
        public static string ReadCAString(BinaryReader reader, Encoding encoding)
        {
            int num = reader.ReadInt16();
            // Unicode is 2 bytes per character; UTF8 is variable, but the number stored is the number of bytes, so use that
            int bytes = (encoding == Encoding.Unicode ? 2 : 1) * num;
            // enough data left?
            if (reader.BaseStream.Length - reader.BaseStream.Position < bytes)
            {
                throw new InvalidDataException(string.Format("Cannot read string of length {0}: only {1} bytes left",
                    bytes, reader.BaseStream.Length - reader.BaseStream.Position));
            }
            return new string(encoding.GetChars(reader.ReadBytes(bytes)));
        }
        /*
         * Read a zero-terminated Unicode string.
         */
        public static string ReadZeroTerminatedUnicode(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(0x200);
            StringBuilder builder = new StringBuilder();
            for (int i = 0; bytes[i] != 0; i += 2)
            {
                builder.Append(Encoding.Unicode.GetChars(bytes, i, 2));
            }
            return builder.ToString();
        }
        /*
         * Read a zero-terminated ASCII string.
         */
        public static string ReadZeroTerminatedAscii(BinaryReader reader)
        {
            StringBuilder builder2 = new StringBuilder();
            byte ch2 = reader.ReadByte();
            while (ch2 != '\0')
            {
                builder2.Append((char)ch2);
                ch2 = reader.ReadByte();
            }
            return builder2.ToString();
        }
        /*
         * Write the given zero-terminated ASCII string to the given writer.
         */
        public static void WriteZeroTerminatedAscii(BinaryWriter writer, string toWrite)
        {
            writer.Write(toWrite.ToCharArray());
            writer.Write((byte)0);
        }
        /*
         * Write the given string to the given writer in Unicode.
         */
        public static void WriteCAString(BinaryWriter writer, string value)
        {
            WriteCAString(writer, value, Encoding.Unicode);
        }
        /*
         * Writer the given string to the given writer in the given encoding.
         * First writes out 2 bytes containing the string length, then the string
         * (not zero-terminated).
         */
        public static void WriteCAString(BinaryWriter writer, string value, Encoding encoding)
        {
            byte[] buffer = encoding.GetBytes(value);
            // utf-8 stores the number of bytes, not characters... inconsistent much?
            int len = (encoding == Encoding.UTF8) ? buffer.Length : value.Length;
            writer.Write((ushort)len);
            writer.Write(encoding.GetBytes(value));
        }
        /*
         * Write the given string to the given writer in unicode (zero-terminated).
         */
        public static void WriteZeroTerminatedUnicode(BinaryWriter writer, string value)
        {
            byte[] array = new byte[0x200];
            Encoding.Unicode.GetBytes(value).CopyTo(array, 0);
            writer.Write(array);
        }

        /*
         * Fills the given list from the given reader with data created by the given item reader.
         */
        public static void FillList<T>(List<T> toFill, ItemReader<T> readItem, BinaryReader reader,
                                          bool skipIndex = true, int itemCount = -1)
        {
            try
            {

#if DEBUG
                long listStartPosition = reader.BaseStream.Position;
#endif
                if (itemCount == -1)
                {
                    itemCount = reader.ReadInt32();
                }
#if DEBUG
                Console.WriteLine("Reading list at {0:x}, {1} entries", listStartPosition, itemCount);
#endif
                for (int i = 0; i < itemCount; i++)
                {
                    try
                    {
                        if (skipIndex)
                        {
                            reader.ReadInt32();
                        }
                        toFill.Add(readItem(reader));
                    }
                    catch (Exception ex)
                    {
                        throw new ParseException(string.Format("Failed to read item {0}", i),
                                                 reader.BaseStream.Position, ex);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new ParseException(string.Format("Failed to entries for list {0}"),
                                         reader.BaseStream.Position, ex);
            }
        }
        /*
         * Delegate for methods reading data from a reader.
         */
        public delegate T ItemReader<T>(BinaryReader reader);
    }


    public class ParseException : Exception
    {
        public long OccurredAt { get; private set; }

        public ParseException(string message, long position) : base(message)
        {
            OccurredAt = position;
        }

        public ParseException(string message, long position, Exception x)
        : base(message, x)
        {
            OccurredAt = position;
        }
    }
}