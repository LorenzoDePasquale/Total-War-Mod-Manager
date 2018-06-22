using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Xml;

namespace Total_War_Mod_Manager
{
    /// <summary>
    /// Logica di interazione per DatabasePreview.xaml
    /// </summary>
    public partial class DatabasePreview : Window
    {
        public DatabasePreview(PackedFile file)
        {
            InitializeComponent();
            DBFile dbfile = PackedFileDbCodec.Decode(file);
            dataGrid1.ItemsSource = dbfile.Entries;
        }
    }



    public interface Codec<T>
    {
        T Decode(Stream file);
    }

    public class PackedFileDbCodec : Codec<DBFile>
    {
        string typeName;

        public delegate void EntryLoaded(FieldInfo info, string value);
        public delegate void HeaderLoaded(DBFileHeader header);
        public delegate void LoadingPackedFile(PackedFile packed);

        /*
         * If set to true (default), codec will add the GUID of a
         * successfully decoded db file to the list of GUIDs
         * which can be decoded.
         */
        public bool AutoadjustGuid { get; set; }

        #region Internal
        // header markers
        static UInt32 GUID_MARKER = BitConverter.ToUInt32(new byte[] { 0xFD, 0xFE, 0xFC, 0xFF }, 0);
        static UInt32 VERSION_MARKER = BitConverter.ToUInt32(new byte[] { 0xFC, 0xFD, 0xFE, 0xFF }, 0);
        #endregion

        /*
         * Retrieve codec for the given PackedFile.
         */
        public static PackedFileDbCodec GetCodec(PackedFile file)
        {
            return new PackedFileDbCodec(DBFile.Typename(file.FullPath));
        }
        /*
         * Create DBFile from the given PackedFile.
         */
        public static DBFile Decode(PackedFile file)
        {
            PackedFileDbCodec codec = FromFilename(file.FullPath);
            return codec.Decode(file.Data);
        }
        /*
         * Create codec for the given file name.
         */
        public static PackedFileDbCodec FromFilename(string filename)
        {
            return new PackedFileDbCodec(DBFile.Typename(filename));
        }
        /*
         * Create codec for table of the given type.
         */
        public PackedFileDbCodec(string type)
        {
            typeName = type;
            AutoadjustGuid = true;
        }

        #region Read
        /*
		 * Reads a db file from stream, using the version information
		 * contained in the header read from it.
		 */
        public DBFile Decode(Stream stream)
        {
            BinaryReader reader = new BinaryReader(stream);
            reader.BaseStream.Position = 0;
            DBFileHeader header = readHeader(reader);
            List<TypeInfo> infos = DBTypeMap.Instance.GetVersionedInfos(typeName, header.Version);
            if (infos.Count == 0)
            {
                infos.AddRange(DBTypeMap.Instance.GetAllInfos(typeName));
            }
            foreach (TypeInfo realInfo in infos)
            {
                try
                {
                    DBFile result = ReadFile(reader, header, realInfo);
                    return result;
                }
                catch (Exception) { }
            }
            return null;
            // throw new DBFileNotSupportedException(string.Format("No applicable type definition found"));
        }
        public DBFile ReadFile(BinaryReader reader, DBFileHeader header, TypeInfo info)
        {
            reader.BaseStream.Position = header.Length;
            DBFile file = new DBFile(header, info);
            int i = 0;
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                try
                {
                    file.Entries.Add(ReadFields(reader, info));
                    i++;
                }
                catch (Exception x)
                {
                    string message = string.Format("{2} at entry {0}, db version {1}", i, file.Header.Version, x.Message);
                    throw new Exception(message, x);
                }
            }
            if (file.Entries.Count != header.EntryCount)
            {
                throw new Exception(string.Format("Expected {0} entries, got {1}", header.EntryCount, file.Entries.Count));
            }
            else if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new Exception(string.Format("Expected {0} bytes, read {1}", header.Length, reader.BaseStream.Position));
            }
            return file;
        }
        /*
         * Decode from the given data array (usually retrieved from a packed file Data).
         */
        public DBFile Decode(byte[] data)
        {
            using (MemoryStream stream = new MemoryStream(data, 0, data.Length))
            {
                return Decode(stream);
            }
        }
        #endregion

        /*
         * Query if given packed file can be deccoded.
         * Is not entirely reliable because it only reads the header and checks if a 
         * type definition is available for the given GUID and/or type name and version.
         * The actual decode tries out all available type infos for that type name
         * but that is less efficient because it has to read the whole file at least once
         * if successful.
         */
        public static bool CanDecode(PackedFile packedFile, out string display)
        {
            bool result = true;
            string key = DBFile.Typename(packedFile.FullPath);
            if (DBTypeMap.Instance.IsSupported(key))
            {
                try
                {
                    DBFileHeader header = PackedFileDbCodec.readHeader(packedFile);
                    int maxVersion = DBTypeMap.Instance.MaxVersion(key);
                    if (maxVersion != 0 && header.Version > maxVersion)
                    {
                        display = string.Format("{0}: needs {1}, has {2}", key, header.Version, DBTypeMap.Instance.MaxVersion(key));
                        result = false;
                    }
                    else
                    {
                        display = string.Format("Version: {0}", header.Version);
                    }
                }
                catch (Exception x)
                {
                    display = string.Format("{0}: {1}", key, x.Message);
                }
            }
            else
            {
                display = string.Format("{0}: no definition available", key);
                result = false;
            }
            return result;
        }

        #region Read Header
        public static DBFileHeader readHeader(PackedFile file)
        {
            using (MemoryStream stream = new MemoryStream(file.Data, (int)0, (int)file.Size))
            {
                return readHeader(stream);
            }
        }
        public static DBFileHeader readHeader(Stream stream)
        {
            return readHeader(new BinaryReader(stream));
        }
        public static DBFileHeader readHeader(BinaryReader reader)
        {
            byte index = reader.ReadByte();
            int version = 0;
            string guid = "";
            bool hasMarker = false;
            uint entryCount = 0;

            try
            {
                if (index != 1)
                {
                    // I don't think those can actually occur more than once per file
                    while (index == 0xFC || index == 0xFD)
                    {
                        var bytes = new List<byte>(4);
                        bytes.Add(index);
                        bytes.AddRange(reader.ReadBytes(3));
                        UInt32 marker = BitConverter.ToUInt32(bytes.ToArray(), 0);
                        if (marker == GUID_MARKER)
                        {
                            guid = IOFunctions.ReadCAString(reader, Encoding.Unicode);
                            index = reader.ReadByte();
                        }
                        else if (marker == VERSION_MARKER)
                        {
                            hasMarker = true;
                            version = reader.ReadInt32();
                            index = reader.ReadByte();
                            // break;
                        }
                        else
                        {
                            throw new Exception(string.Format("could not interpret {0}", marker));
                        }
                    }
                }
                entryCount = reader.ReadUInt32();
            }
            catch
            {
            }
            DBFileHeader header = new DBFileHeader(guid, version, entryCount, hasMarker);
            return header;
        }
        #endregion

        // creates a list of field values from the given type.
        // stream needs to be positioned at the beginning of the entry.
        private DBRow ReadFields(BinaryReader reader, TypeInfo ttype, bool skipHeader = true)
        {
            if (!skipHeader)
            {
                readHeader(reader);
            }
            List<FieldInstance> entry = new List<FieldInstance>();
            for (int i = 0; i < ttype.Fields.Count; ++i)
            {
                FieldInfo field = ttype.Fields[i];

                FieldInstance instance = null;
                try
                {
                    instance = field.CreateInstance();
                    instance.Decode(reader);
                    entry.Add(instance);
                }
                catch (Exception x)
                {
                    throw new InvalidDataException(string.Format
                        ("Failed to read field {0}/{1}, type {3} ({2})", i, ttype.Fields.Count, x.Message, instance.Info.TypeName));
                }
            }
            DBRow result = new DBRow(ttype, entry);
            return result;
        }
    }

    public class DBTypeMap : IEnumerable<TypeInfo>
    {
        public static readonly string MASTER_SCHEMA_FILE_NAME = @"C:\Users\Lorenzo\Desktop\Pack File Manager 5.0\master_schema.xml";
        public static readonly string SCHEMA_USER_FILE_NAME = "schema_user.xml";
        public static readonly string MODEL_SCHEMA_FILE_NAME = "schema_models.xml";

        List<TypeInfo> typeInfos = new List<TypeInfo>();
        public List<TypeInfo> AllInfos
        {
            get
            {
                return typeInfos;
            }
        }

        /*
         * Singleton access.
         */
        static readonly DBTypeMap instance = new DBTypeMap();
        public static DBTypeMap Instance
        {
            get
            {
                return instance;
            }
        }
        private DBTypeMap()
        {
            // prevent instantiation
        }

        /*
         * Query if any schema has been loaded.
         */
        public bool Initialized
        {
            get
            {
                return typeInfos.Count != 0;
            }
        }

        /*
         * A list of schema files that may be present to contain schema data.
         * The user file is intended to store the data for a user after he has edited a field name or suchlike
         * so he is able to send it in for us to integrate; but since now schema files are split up per game,
         * that is not really viable anymore because it would only work for the game he currently saved as.
         */
        public static readonly string[] SCHEMA_FILENAMES = {
            SCHEMA_USER_FILE_NAME, MASTER_SCHEMA_FILE_NAME
        };

        /*
         * Retrieve all infos currently loaded for the given table,
         * either in the table/version format or from the GUID list.
         */
        public List<TypeInfo> GetAllInfos(string table)
        {
            List<TypeInfo> result = new List<TypeInfo>();
            typeInfos.ForEach(t => {
                if (t.Name.Equals(table))
                {
                    result.Add(t);
                }
            });
            return result;
        }

        public List<TypeInfo> GetVersionedInfos(string key, int version)
        {
            List<TypeInfo> result = new List<TypeInfo>(GetAllInfos(key));
            result.Sort(new BestVersionComparer { TargetVersion = version });
            return result;
        }

        #region Initialization / IO
        /*
         * Read schema from given directory, in the order of the SCHEMA_FILENAMES.
         */
        public void InitializeTypeMap(string basePath)
        {
            foreach (string file in SCHEMA_FILENAMES)
            {
                string xmlFile = Path.Combine(basePath, file);
                if (File.Exists(xmlFile))
                {
                    initializeFromFile(xmlFile);
                    break;
                }
            }
        }
        /*
         * Load the given schema xml file.
         */
        public void initializeFromFile(string filename)
        {
            XmlImporter importer = null;
            using (Stream stream = File.OpenRead(filename))
            {
                importer = new XmlImporter(stream);
                importer.Import(true);
            }
            typeInfos = importer.Imported;
            if (File.Exists(MODEL_SCHEMA_FILE_NAME))
            {
                importer = null;
                using (Stream stream = File.OpenRead(MODEL_SCHEMA_FILE_NAME))
                {
                    importer = new XmlImporter(stream);
                    importer.Import();
                }
            }
        }

        #endregion

        #region Setting Changed Definitions
        public void SetByName(string key, List<FieldInfo> setTo)
        {
            typeInfos.Add(new TypeInfo(setTo)
            {
                Name = key
            });
        }
        #endregion

        #region Utilities
        public string GetUserFilename(string suffix)
        {
            return string.Format(string.Format("schema_{0}.xml", suffix));
        }
        #endregion

        #region Supported Type/Version Queries
        /*
         * Retrieve all supported Type Names.
         */
        public List<string> DBFileTypes
        {
            get
            {
                SortedSet<string> result = new SortedSet<string>();
                typeInfos.ForEach(t => {
                    if (!result.Contains(t.Name))
                    {
                        result.Add(t.Name);
                    }
                });
                return new List<string>(result);
            }
        }

        /*
         * Retrieve the highest version for the given type.
         */
        public int MaxVersion(string type)
        {
            int result = 0;
            typeInfos.ForEach(t => { if (t.Name == type) { result = Math.Max(t.Version, result); } });
            return result;
        }
        /*
         * Query if the given type is supported at all.
         */
        public bool IsSupported(string type)
        {
            foreach (TypeInfo info in typeInfos)
            {
                if (info.Name.Equals(type))
                {
                    return true;
                }
            }
            return false;
        }
        #endregion

        /*
         * Note:
         * The names of the TypeInfos iterated here cannot be changed using the
         * enumeration; the FieldInfo lists and contained FieldInfos can.
         */
        public IEnumerator<TypeInfo> GetEnumerator()
        {
            return typeInfos.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /*
     * Class defining a db type by GUID. They do still carry their type name
     * and a version number along; the name/version tuple may not be unique though.
     */
    public class GuidTypeInfo : IComparable<GuidTypeInfo>
    {
        public GuidTypeInfo(string guid) : this(guid, "", 0) { }
        public GuidTypeInfo(string guid, string type, int version)
        {
            Guid = guid;
            TypeName = type;
            Version = version;
        }
        public string Guid { get; set; }
        public string TypeName { get; set; }
        public int Version { get; set; }
        /*
         * Comparable (mostly to sort the master schema for easier version control).
         */
        public int CompareTo(GuidTypeInfo other)
        {
            int result = TypeName.CompareTo(other.TypeName);
            if (result == 0)
            {
                result = Version - other.Version;
            }
            if (result == 0)
            {
                result = Guid.CompareTo(other.Guid);
            }
            return result;
        }
        #region Framework overrides
        public override bool Equals(object obj)
        {
            bool result = obj is GuidTypeInfo;
            if (result)
            {
                if (string.IsNullOrEmpty(Guid))
                {
                    result = (obj as GuidTypeInfo).TypeName.Equals(TypeName);
                    result &= (obj as GuidTypeInfo).Version.Equals(Version);
                }
                else
                {
                    result = (obj as GuidTypeInfo).Guid.Equals(Guid);
                }
            }
            return result;
        }
        public override int GetHashCode()
        {
            return Guid.GetHashCode();
        }
        public override string ToString()
        {
            return string.Format("{1}/{2} # {0}", Guid, TypeName, Version);
        }
        #endregion
    }

    /*
     * Comparer for two guid info instances.
     */
    class GuidInfoComparer : Comparer<GuidTypeInfo>
    {
        public override int Compare(GuidTypeInfo x, GuidTypeInfo y)
        {
            int result = x.TypeName.CompareTo(y.TypeName);
            if (result == 0)
            {
                result = y.Version - x.Version;
            }
            return result;
        }
    }

    /*
     * Compares two versioned infos to best match a version being looked for.
     */
    class BestVersionComparer : IComparer<TypeInfo>
    {
        public int TargetVersion { get; set; }
        public int Compare(TypeInfo info1, TypeInfo info2)
        {
            int difference1 = info1.Version - TargetVersion;
            int difference2 = info2.Version - TargetVersion;
            return difference2 - difference1;
        }
    }


    public class DBFileHeader
    {
        public string GUID { get; set; }
        public bool HasVersionMarker { get; set; }
        public int Version { get; set; }
        public uint EntryCount { get; set; }
        /*
         * The length of the encoded header.
         */
        public int Length
        {
            get
            {
                int result = 5;
                result += (GUID.Length != 0) ? 78 : 0;
                result += HasVersionMarker ? 8 : 0;
                return result;
            }
        }
        /*
         * Create header with the given GUID, version and entry count.
         */
        public DBFileHeader(string guid, int version, uint entryCount, bool marker)
        {
            GUID = guid;
            Version = version;
            EntryCount = entryCount;
            HasVersionMarker = marker;
        }

        /*
         * Create copy of given header.
         */
        public DBFileHeader(DBFileHeader toCopy) : this(toCopy.GUID, toCopy.Version, 0, toCopy.HasVersionMarker) { }

        #region Framework Overrides
        public override bool Equals(object other)
        {
            bool result = false;
            if (other is DBFileHeader)
            {
                DBFileHeader header2 = (DBFileHeader)other;
                result = GUID.Equals(header2.GUID);
                result &= Version.Equals(header2.Version);
                result &= EntryCount.Equals(header2.EntryCount);
            }
            return result;
        }
        public override int GetHashCode()
        {
            return GUID.GetHashCode();
        }
        #endregion
    }

    /*
	 * Class representing a database file.
	 */
    public class DBFile
    {
        private List<DBRow> entries = new List<DBRow>();
        public DBFileHeader Header;
        public TypeInfo CurrentType
        {
            get;
            set;
        }

        #region Attributes
        // the entries of this file
        public List<DBRow> Entries
        {
            get
            {
                return this.entries;
            }
        }

        // access by row/column
        public FieldInstance this[int row, int column]
        {
            get
            {
                return entries[row][column];
            }
        }
        #endregion

        #region Constructors
        /*
         * Create db file with the given header and the given type.
         */
        public DBFile(DBFileHeader h, TypeInfo info)
        {
            Header = h;
            CurrentType = info;
        }
        /*
         * Create copy of the given db file.
         */
        public DBFile(DBFile toCopy) : this(toCopy.Header, toCopy.CurrentType)
        {
            Header = new DBFileHeader(toCopy.Header.GUID, toCopy.Header.Version, toCopy.Header.EntryCount, toCopy.Header.HasVersionMarker);
            // we need to create a new instance for every field so we don't write through to the old data
            toCopy.entries.ForEach(entry => entries.Add(new DBRow(toCopy.CurrentType, entry)));
        }
        #endregion

        /*
         * Create new entry for the data base.
         * Note that the entry will not be added to the entries by this.
         */
        public DBRow GetNewEntry()
        {
            return new DBRow(CurrentType);
        }

        /*
         * Add data contained in the given db file to this one.
         */
        public void Import(DBFile file)
        {
            if (CurrentType.Name != file.CurrentType.Name)
            {
                throw new Exception ("File type of imported DB doesn't match that of the currently opened one");
            }
            // check field type compatibility
            for (int i = 0; i < file.CurrentType.Fields.Count; i++)
            {
                if (file.CurrentType.Fields[i].TypeCode != CurrentType.Fields[i].TypeCode)
                {
                    throw new Exception ("Data structure of imported DB doesn't match that of currently opened one at field " + i);
                }
            }
            DBFileHeader h = file.Header;
            Header = new DBFileHeader(h.GUID, h.Version, h.EntryCount, h.HasVersionMarker);
            CurrentType = file.CurrentType;
            // this.entries = new List<List<FieldInstance>> ();
            entries.AddRange(file.entries);
            Header.EntryCount = (uint)entries.Count;
        }
        /*
         * Helper to retrieve type name from a file path.
         */
        public static string Typename(string fullPath)
        {
            return Path.GetFileName(Path.GetDirectoryName(fullPath));
        }
    }


    public class DBRow : List<FieldInstance>
    {
        private TypeInfo info;

        public DBRow(TypeInfo i, List<FieldInstance> val) : base(val)
        {
            info = i;
        }
        public DBRow(TypeInfo i) : this(i, CreateRow(i)) { }

        public TypeInfo Info
        {
            get
            {
                return info;
            }
        }

        public FieldInstance this[string fieldName]
        {
            get
            {
                return this[IndexOfField(fieldName)];
            }
            set
            {
                this[IndexOfField(fieldName)] = value;
            }
        }
        private int IndexOfField(string fieldName)
        {
            for (int i = 0; i < info.Fields.Count; i++)
            {
                if (info.Fields[i].Name.Equals(fieldName))
                {
                    return i;
                }
            }
            throw new IndexOutOfRangeException(string.Format("Field name {0} not valid for type {1}", fieldName, info.Name));
        }

        public static List<FieldInstance> CreateRow(TypeInfo info)
        {
            List<FieldInstance> result = new List<FieldInstance>(info.Fields.Count);
            info.Fields.ForEach(f => result.Add(f.CreateInstance()));
            return result;
        }
    }

    public class TypeVersionTuple
    {
        public string Type { get; set; }
        public int MaxVersion { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public class TypeInfo : IComparable<TypeInfo>
    {
        public string Name
        {
            get; set;
        }
        int version = 0;
        public int Version
        {
            get
            {
                return version;
            }
            set
            {
                version = value;
            }
        }
        List<FieldInfo> fields = new List<FieldInfo>();
        public List<FieldInfo> Fields
        {
            get
            {
                return fields;
            }
        }
        public FieldInfo this[string name]
        {
            get
            {
                FieldInfo result = null;
                foreach (FieldInfo field in fields)
                {
                    if (field.Name.Equals(name))
                    {
                        result = field;
                        break;
                    }
                }
                return result;
            }
        }

        #region Constructors
        public TypeInfo()
        {
        }

        public TypeInfo(List<FieldInfo> addFields)
        {
            Fields.AddRange(addFields);
        }

        public TypeInfo(TypeInfo toCopy)
        {
            Name = toCopy.Name;
            Version = toCopy.Version;
            Fields.AddRange(toCopy.Fields);
        }
        #endregion

        public bool SameTypes(TypeInfo other)
        {
            bool typesMatch = fields.Count == other.fields.Count;
            if (typesMatch)
            {
                for (int i = 0; i < fields.Count && typesMatch; i++)
                {
                    if (!fields[i].TypeName.Equals(other.fields[i].TypeName))
                    {
                        typesMatch = false;
                    }
                }
            }
            return typesMatch;
        }

        public int CompareTo(TypeInfo other)
        {
            int result = Name.CompareTo(other.Name);
            if (result == 0)
            {
                result = Version - other.Version;
            }
            if (result == 0)
            {
                result = Fields.Count - other.Fields.Count;
            }
            if (result == 0)
            {
                for (int i = 0; i < Fields.Count; i++)
                {
                    result = Fields[i].Name.CompareTo(other.Fields[i].Name);
                    if (result == 0)
                    {
                        result = Fields[i].TypeName.CompareTo(other.Fields[i].TypeName);
                    }
                    if (result != 0)
                    {
                        break;
                    }
                }
            }
            return result;
        }

        public override string ToString()
        {
            return string.Format("Name={0}, Version={1}, {2} Fields", Name, Version, Fields.Count);
        }
    }

    public class Types
    {
        public static FieldInfo FromTypeName(string typeName)
        {
            switch (typeName)
            {
                case "string":
                    return StringType();
                case "string_ascii":
                    return StringTypeAscii();
                case "optstring":
                    return OptStringType();
                case "optstring_ascii":
                    return OptStringTypeAscii();
                case "int":
                case "integer":
                case "autonumber":
                    return IntType();
                case "short":
                    return ShortType();
                case "float":
                case "single":
                case "decimal":
                case "double":
                    return SingleType();
                // return DoubleType ();
                case "boolean":
                case "yesno":
                    return BoolType();
                case "list":
                    return ListType();
            }
            if (typeName.StartsWith("blob"))
            {
                string lengthPart = typeName.Substring(4);
                int length = int.Parse(lengthPart);
                return new VarBytesType(length);
            }
            throw new InvalidOperationException(String.Format("Cannot create field info from {0}", typeName));
        }
        public static FieldInfo StringType() { return new StringType() { Name = "unknown" }; }
        public static FieldInfo StringTypeAscii() { return new StringTypeAscii() { Name = "unknown" }; }
        public static FieldInfo IntType() { return new IntType() { Name = "unknown" }; }
        public static FieldInfo ShortType() { return new ShortType() { Name = "unknown" }; }
        public static FieldInfo BoolType() { return new BoolType() { Name = "unknown" }; }
        public static FieldInfo OptStringType() { return new OptStringType() { Name = "unknown" }; }
        public static FieldInfo OptStringTypeAscii() { return new OptStringTypeAscii() { Name = "unknown" }; }
        public static FieldInfo SingleType() { return new SingleType() { Name = "unknown" }; }
        public static FieldInfo DoubleType() { return new DoubleType() { Name = "unknown" }; }
        public static FieldInfo ByteType() { return new VarBytesType(1) { Name = "unknown" }; }
        public static FieldInfo ListType() { return new ListType() { Name = "unknown" }; }
    }

    /*
     * A reference to a field in a specific table.
     */
    public class FieldReference
    {
        static char[] SEPARATOR = { '.' };

        /*
         * Create reference to given table and field.
         */
        public FieldReference(string table, string field)
        {
            Table = table;
            Field = field;
        }
        /*
         * Parse encoded reference (see #FormatReference).
         */
        public FieldReference(string encoded)
        {
            string[] parts = encoded.Split(SEPARATOR);
            Table = parts[0];
            Field = parts[1];
        }
        /*
         * Create an empty reference.
         */
        public FieldReference()
        {
        }

        public string Table { get; set; }
        public string Field { get; set; }

        public override string ToString()
        {
            string result = "";
            if (!string.IsNullOrEmpty(Table) && !string.IsNullOrEmpty(Field))
            {
                result = FormatReference(Table, Field);
            }
            return result;
        }

        /*
         * Encode given table and field to format "table.field".
         */
        public static string FormatReference(string table, string field)
        {
            return string.Format("{0}.{1}", table, field);
        }
    }

    /*
     * The info determining a column of a db table.
     */
    [System.Diagnostics.DebuggerDisplay("{Name} - {TypeName}; {Optional}")]
    public abstract class FieldInfo
    {
        /*
         * The column name.
         */
        public string Name
        {
            get;
            set;
        }
        public virtual string TypeName { get; set; }
        public TypeCode TypeCode { get; set; }

        /*
         * Primary keys have to be unique amonst a given table data set.
         * There may be more than one primary key, in which case the combination
         * of their values has to be unique.
         */
        public bool PrimaryKey { get; set; }
        /*
         * There are string fields which do not need to contain data, in which
         * case they will only contain a "0" in the packed file.
         * This attribute is true for those fields.
         */
        public bool Optional { get; set; }

        #region Reference
        /*
         * The referenced table/field containing the valid values for this column.
         */
        FieldReference reference;
        public FieldReference FieldReference
        {
            get
            {
                return reference;
            }
            set
            {
                reference = value;
            }
        }
        /*
         * The referenced table/field as a string.
         */
        public string ForeignReference
        {
            get
            {
                return reference != null ? reference.ToString() : "";
            }
            set
            {
                reference = new FieldReference(value);
            }
        }

        // The referenced table; empty string if no reference
        public string ReferencedTable
        {
            get
            {
                return reference != null ? reference.Table : "";
            }
        }
        // The referenced field in the referenced table; empty string if no reference
        public string ReferencedField
        {
            get
            {
                return reference != null ? reference.Field : "";
            }
            set
            {
                reference.Field = value;
            }
        }
        #endregion

        /*
         * Create an instance valid for this field.
         */
        public abstract FieldInstance CreateInstance();

        #region Framework Overrides
        public override bool Equals(object other)
        {
            bool result = false;
            if (other is FieldInfo)
            {
                FieldInfo info = other as FieldInfo;
                result = Name.Equals(info.Name);
                result &= TypeName.Equals(info.TypeName);
            }
            return result;
        }

        public override int GetHashCode()
        {
            return 2 * Name.GetHashCode() +
                3 * TypeName.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("{0}:{1}", Name, TypeName);
        }
        #endregion
    }

    class StringType : FieldInfo
    {
        public StringType()
        {
            TypeName = "string";
            TypeCode = TypeCode.String;
        }
        public override FieldInstance CreateInstance()
        {
            return new StringField()
            {
                Name = this.Name,
                Value = ""
            };
        }
    }
    class StringTypeAscii : FieldInfo
    {
        public StringTypeAscii()
        {
            TypeName = "string_ascii";
            TypeCode = TypeCode.String;
        }
        public override FieldInstance CreateInstance()
        {
            return new StringFieldAscii()
            {
                Name = this.Name,
                Value = ""
            };
        }
    }

    class IntType : FieldInfo
    {
        public IntType()
        {
            TypeName = "int";
            TypeCode = TypeCode.Int32;
        }
        public override FieldInstance CreateInstance()
        {
            return new IntField()
            {
                Name = this.Name,
                Value = "0"
            };
        }
    }

    class ShortType : FieldInfo
    {
        public ShortType()
        {
            TypeName = "short";
            TypeCode = TypeCode.Int16;
        }
        public override FieldInstance CreateInstance()
        {
            return new ShortField()
            {
                Name = this.Name,
                Value = "0"
            };
        }
    }

    class SingleType : FieldInfo
    {
        public SingleType()
        {
            TypeName = "float";
            TypeCode = TypeCode.Single;
        }
        public override FieldInstance CreateInstance()
        {
            return new SingleField()
            {
                Name = this.Name,
                Value = "0"
            };
        }
    }

    class DoubleType : FieldInfo
    {
        public DoubleType()
        {
            TypeName = "double";
            TypeCode = TypeCode.Single;
        }
        public override FieldInstance CreateInstance()
        {
            return new DoubleField()
            {
                Name = this.Name,
                Value = "0"
            };
        }
    }

    class BoolType : FieldInfo
    {
        public BoolType()
        {
            TypeName = "boolean";
            TypeCode = TypeCode.Boolean;
        }
        public override FieldInstance CreateInstance()
        {
            return new BoolField()
            {
                Name = this.Name,
                Value = "false"
            };
        }
    }

    class OptStringType : FieldInfo
    {
        public OptStringType()
        {
            TypeName = "optstring";
            TypeCode = TypeCode.String;
        }
        public override FieldInstance CreateInstance()
        {
            return new OptStringField()
            {
                Name = this.Name,
                Value = ""
            };
        }
    }
    class OptStringTypeAscii : FieldInfo
    {
        public OptStringTypeAscii()
        {
            TypeName = "optstring_ascii";
            TypeCode = TypeCode.String;
        }
        public override FieldInstance CreateInstance()
        {
            return new OptStringFieldAscii()
            {
                Name = this.Name,
                Value = ""
            };
        }
    }

    public class VarBytesType : FieldInfo
    {
        int byteCount;
        public VarBytesType(int bytes)
        {
            TypeName = string.Format("blob{0}", byteCount);
            TypeCode = TypeCode.Empty;
            byteCount = bytes;
        }
        public override FieldInstance CreateInstance()
        {
            return new VarByteField(byteCount)
            {
                Name = this.Name
            };
        }
    }

    public class ListType : FieldInfo
    {
        public ListType()
        {
            TypeName = "list";
            TypeCode = TypeCode.Object;
        }

        public override string TypeName
        {
            get
            {
                return "list";
            }
        }

        List<FieldInfo> containedInfos = new List<FieldInfo>();
        public List<FieldInfo> Infos
        {
            get
            {
                return containedInfos;
            }
            set
            {
                containedInfos.Clear();
                if (value != null)
                {
                    containedInfos.AddRange(value);
                }
            }
        }
        public override FieldInstance CreateInstance()
        {
            ListField field = new ListField(this);
            // containedInfos.ForEach(i => field.Contained.Add(i.CreateInstance()));
            return field;
        }
        public List<FieldInstance> CreateContainedInstance()
        {
            List<FieldInstance> result = new List<FieldInstance>();
            containedInfos.ForEach(i => result.Add(i.CreateInstance()));
            return result;
        }

        public bool EncodeItemIndices
        {
            get
            {
                return false;
            }
            set
            {
                // ignore
            }
        }

        int itemIndexAt = -1;
        public int ItemIndexAt
        {
            get { return itemIndexAt >= Infos.Count ? -1 : itemIndexAt; }
            set { itemIndexAt = value; }
        }
        int nameAt = -1;
        public int NameAt
        {
            get
            {
                int result = nameAt >= Infos.Count ? -1 : nameAt;
                if (result == -1)
                {
                    // use the first string we find
                    for (int i = 0; i < Infos.Count; i++)
                    {
                        if (Infos[i].TypeCode == System.TypeCode.String)
                        {
                            result = i;
                            break;
                        }
                    }
                }
                return result;
            }
            set { nameAt = value; }
        }

        public override bool Equals(object other)
        {
            bool result = base.Equals(other);
            if (result)
            {
                ListType type = other as ListType;
                result &= type.containedInfos.Count == containedInfos.Count;
                if (result)
                {
                    for (int i = 0; i < containedInfos.Count; i++)
                    {
                        result &= containedInfos[i].Equals(type.containedInfos[i]);
                    }
                }
            }
            return result;
        }

        public override int GetHashCode()
        {
            return 2 * Name.GetHashCode() + 3 * Infos.GetHashCode();
        }
    }


    public abstract class FieldInstance
    {
        public FieldInstance(FieldInfo fieldInfo, string value = "")
        {
            Info = fieldInfo;
            Value = value;
        }

        public FieldInfo Info { get; private set; }
        public string Name
        {
            get
            {
                return Info.Name;
            }
            set
            {
                Info.Name = value;
            }
        }

        /*
         * The encoded value of this instance.
         * Subclasses can override the Value member to provide own decoding/encoding
         * to their actual data type.
         */
        string val;
        public virtual string Value
        {
            get
            {
                return val;
            }
            set { val = value; }
        }
        /*
         * Query the decoded data's length in bytes for this field value.
         */
        public virtual int Length
        {
            get; protected set;
        }
        public virtual int ReadLength
        {
            get
            {
                return Length;
            }
        }

        /*
         * Only provided in CA xml files, not needed for binary decoding.
         */
        public bool RequiresTranslation { get; set; }

        /*
         * Create a copy of this field value.
         */
        public virtual FieldInstance CreateCopy()
        {
            FieldInstance copy = Info.CreateInstance();
            copy.Value = Value;
            return copy;
        }

        public abstract void Encode(BinaryWriter writer);
        public abstract void Decode(BinaryReader reader);

        #region Framework Overrides
        public override string ToString()
        {
            return Value;
        }
        public override bool Equals(object o)
        {
            bool result = o is FieldInstance;
            if (result)
            {
                result = Value.Equals((o as FieldInstance).Value);
            }
            return result;
        }
        public override int GetHashCode()
        {
            return 2 * Info.GetHashCode() + 3 * Value.GetHashCode();
        }
        #endregion
    }

    /*
     * String Field.
     */
    public class StringField : FieldInstance
    {
        protected Encoding stringEncoding = Encoding.Unicode;

        public StringField() : this(Types.StringType()) { }
        public StringField(FieldInfo info) : base(info, "") { }
        public override int Length
        {
            get
            {
                return stringEncoding.GetBytes(Value).Length;
            }
        }
        public override int ReadLength
        {
            get
            {
                return Length + 2;
            }
        }
        public override void Decode(BinaryReader reader)
        {
            Value = IOFunctions.ReadCAString(reader, stringEncoding);
        }
        public override void Encode(BinaryWriter writer)
        {
            IOFunctions.WriteCAString(writer, Value.Trim(), stringEncoding);
        }
    }

    /*
     * It's actually StringFieldUTF8, but I'm not going to rename it now.
     */
    public class StringFieldAscii : StringField
    {
        public StringFieldAscii() : base(Types.StringTypeAscii())
        {
            stringEncoding = Encoding.UTF8;
        }
    }

    /*
     * 4 byte Int Field.
     */
    public class IntField : FieldInstance
    {
        public IntField() : base(Types.IntType(), "0") { Length = 4; }
        public override void Decode(BinaryReader reader)
        {
            Value = reader.ReadInt32().ToString();
        }
        public override void Encode(BinaryWriter writer)
        {
            writer.Write(int.Parse(Value));
        }
        public override string Value
        {
            get
            {
                return base.Value;
            }
            set
            {
                base.Value = string.IsNullOrEmpty(value) ? "0" : int.Parse(value).ToString();
            }
        }
    }

    /*
     * 2-byte Short Field.
     */
    public class ShortField : FieldInstance
    {
        public ShortField() : base(Types.ShortType(), "0") { Length = 2; }
        public override void Decode(BinaryReader reader)
        {
            Value = reader.ReadUInt16().ToString();
        }
        public override void Encode(BinaryWriter writer)
        {
            writer.Write(short.Parse(Value));
        }
        public override string Value
        {
            get
            {
                return base.Value;
            }
            set
            {
                base.Value = short.Parse(value).ToString();
            }
        }
    }

    /*
     * Single Field.
     */
    public class SingleField : FieldInstance
    {
        public SingleField() : base(Types.SingleType(), "0") { Length = 4; }
        public override void Decode(BinaryReader reader)
        {
            Value = reader.ReadSingle().ToString();
        }
        public override void Encode(BinaryWriter writer)
        {
            writer.Write(float.Parse(Value));
        }
        public override string Value
        {
            get
            {
                return base.Value;
            }
            set
            {
                base.Value = float.Parse(value).ToString();
            }
        }
    }

    public class DoubleField : FieldInstance
    {
        public DoubleField() : base(Types.DoubleType(), "0") { Length = 8; }
        public override void Decode(BinaryReader reader)
        {
            Value = reader.ReadDouble().ToString();
        }
        public override void Encode(BinaryWriter writer)
        {
            writer.Write(double.Parse(Value));
        }
        public override string Value
        {
            get
            {
                return base.Value;
            }
            set
            {
                base.Value = double.Parse(value).ToString();
            }
        }
    }

    /*
     * Bool Field.
     */
    public class BoolField : FieldInstance
    {
        public BoolField() : base(Types.BoolType(), false.ToString()) { Length = 1; }
        public override void Decode(BinaryReader reader)
        {
            byte b = reader.ReadByte();
            if (b == 0 || b == 1)
            {
                Value = Convert.ToBoolean(b).ToString();
            }
            else
            {
                throw new InvalidDataException("- invalid - ({0:x2})");
            }
        }
        public override void Encode(BinaryWriter writer)
        {
            writer.Write(bool.Parse(Value));
        }
        public override string Value
        {
            get
            {
                return base.Value;
            }
            set
            {
                base.Value = bool.Parse(value).ToString();
            }
        }
    }

    /*
     * Opt String Field.
     */
    public class OptStringField : FieldInstance
    {
        private bool readLengthZero = false;
        protected Encoding stringEncoding = Encoding.Unicode;
        public OptStringField() : base(Types.OptStringType()) { }
        public OptStringField(FieldInfo info) : base(info) { }
        public override void Decode(BinaryReader reader)
        {
            string result = "";
            byte b = reader.ReadByte();
            if (b == 1)
            {
                result = IOFunctions.ReadCAString(reader, stringEncoding);
                readLengthZero = result.Length == 0;
            }
            else if (b != 0)
            {
                throw new InvalidDataException(string.Format("- invalid - ({0:x2})", b));
            }
            Value = result;
        }

        public override int Length
        {
            get
            {
                return stringEncoding.GetBytes(Value).Length;
            }
        }
        public override int ReadLength
        {
            get
            {
                if (readLengthZero)
                {
                    return 3;
                }
                else
                {
                    // 1 byte for true/false, two for string length if not empty
                    return base.ReadLength + (Value.Length == 0 ? 1 : 3);
                }
            }
        }
        public override void Encode(BinaryWriter writer)
        {
            writer.Write(Value.Length > 0);
            if (Value.Length > 0)
            {
                IOFunctions.WriteCAString(writer, Value.Trim(), stringEncoding);
            }
        }
    }
    public class OptStringFieldAscii : OptStringField
    {
        public OptStringFieldAscii() : base(Types.OptStringTypeAscii())
        {
            stringEncoding = Encoding.UTF8;
        }
    }

    /*
     * VarByte Field.
     */
    public class VarByteField : FieldInstance
    {
        public VarByteField() : this(1) { }
        public VarByteField(int len) : base(Types.ByteType()) { Length = len; }
        public override void Decode(BinaryReader reader)
        {
            if (Length == 0)
            {
                Value = "";
                return;
            }
            byte[] bytes = reader.ReadBytes(Length);
            StringBuilder result = new StringBuilder(3 * bytes.Length);
            result.Append(string.Format("{0:x2}", bytes[0]));
            for (int i = 1; i < bytes.Length; i++)
            {
                result.Append(string.Format(" {0:x2}", bytes[i]));
            }
            base.Value = result.ToString();
        }
        public override void Encode(BinaryWriter writer)
        {
            string[] split = Value.Split(' ');
            foreach (string s in split)
            {
                writer.Write(byte.Parse(s, System.Globalization.NumberStyles.HexNumber));
            }
        }
        public override string Value
        {
            get
            {
                return base.Value;
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    base.Value = "";
                }
                else
                {
#if DEBUG
                    Console.WriteLine("parsing '{0}' as byte", value);
#endif
                    StringBuilder result = new StringBuilder(value.Length);
                    string[] split = value.Split(' ');
                    result.Append(string.Format("{0}", byte.Parse(split[0]).ToString()));
                    for (int i = 1; i < split.Length; i++)
                    {
                        result.Append(string.Format(" {0}", byte.Parse(split[1]).ToString("x2")));
                    }
                    base.Value = result.ToString();
                }
            }
        }
    }


    /*
     * List Field.
     */
    public class ListField : FieldInstance
    {
        public ListField(ListType type) : base(type) { }

        public override string Value
        {
            get
            {
                return string.Format("{0} entries, length {1}", contained.Count, Length);
            }
        }

        public override int Length
        {
            get
            {
                // the item count
                int result = 4;
                // the items' indices, if applicable
                if ((Info as ListType).EncodeItemIndices)
                {
                    result += contained.Count * 4;
                }
                // added length of all contained items
                contained.ForEach(i => i.ForEach(f => result += f.Length));
                return result;
            }
        }

        private List<List<FieldInstance>> contained = new List<List<FieldInstance>>();
        public List<List<FieldInstance>> Contained
        {
            get
            {
                return contained;
            }
        }

        public ListType ContainerType
        {
            get
            {
                return Info as ListType;
            }
        }

        public override FieldInstance CreateCopy()
        {
            ListField field = new ListField(Info as ListType);
            contained.ForEach(l => {
                List<FieldInstance> clone = new List<FieldInstance>(l.Count);
                l.ForEach(i => clone.Add(i.CreateCopy()));
                field.Contained.Add(clone);
            });
            return field;
        }

        public override void Encode(BinaryWriter writer)
        {
            writer.Write(contained.Count);
            for (int i = 0; i < contained.Count; i++)
            {
                if (ContainerType.EncodeItemIndices)
                {
                    writer.Write(i);
                }
                foreach (FieldInstance field in contained[i])
                {
                    field.Encode(writer);
                }
            }
        }

        public override void Decode(BinaryReader reader)
        {
            contained.Clear();
            int itemCount = reader.ReadInt32();
            contained.Capacity = itemCount;
            for (int i = 0; i < itemCount; i++)
            {
                if (ContainerType.EncodeItemIndices)
                {
                    reader.ReadInt32();
                }
                List<FieldInstance> entry = new List<FieldInstance>(ContainerType.Infos.Count);
                foreach (FieldInfo info in ContainerType.Infos)
                {
                    FieldInstance field = info.CreateInstance();
                    field.Decode(reader);
                    entry.Add(field);
                }
                contained.Add(entry);
            }
        }
    }

    
    public class XmlImporter
    {
        // table to contained fields
        List<TypeInfo> typeInfos = new List<TypeInfo>();
        public List<TypeInfo> Imported
        {
            get
            {
                return typeInfos;
            }
        }

        TextReader reader;
        public XmlImporter(Stream stream)
        {
            reader = new StreamReader(stream);
        }

        static string UnifyName(string name)
        {
            return name.ToLower().Replace(" ", "_");
        }

        public void Import(bool unify = false)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(reader);
            foreach (XmlNode node in doc.ChildNodes)
            {
                foreach (XmlNode tableNode in node.ChildNodes)
                {
                    string id;
                    int version = 0;
                    List<FieldInfo> fields = new List<FieldInfo>();
                    // bool verifyEquality = false;

                    XmlAttribute attribute = tableNode.Attributes["name"];
                    if (attribute != null)
                    {
                        // pre-GUID table
                        id = attribute.Value;
                        if (unify)
                        {
                            id = UnifyName(id);
                        }
                    }
                    else
                    {
                        id = tableNode.Attributes["table_name"].Value.Trim();
                        string table_version = tableNode.Attributes["table_version"].Value.Trim();
                        version = int.Parse(table_version);
                    }

                    FillFieldList(fields, tableNode.ChildNodes, unify);
                    TypeInfo info = new TypeInfo(fields)
                    {
                        Name = id,
                        Version = version
                    };
#if DEBUG
                    // Console.WriteLine("Adding table {0} version {1}", info.Name, info.Version);
#endif
                    typeInfos.Add(info);
                }
            }
        }

        void FillFieldList(List<FieldInfo> fields, XmlNodeList nodes, bool unify = false)
        {
            // add all fields
            foreach (XmlNode fieldNode in nodes)
            {
                FieldInfo field = FromNode(fieldNode, unify);
                if (unify)
                {
                    field.Name = UnifyName(field.Name);
                }
                fields.Add(field);
            }
        }

        /* 
         * Collect the given node's attributes and create a field from them. 
         */
        FieldInfo FromNode(XmlNode fieldNode, bool unify)
        {
            FieldInfo description = null;
            try
            {
                XmlAttributeCollection attributes = fieldNode.Attributes;
                string name = attributes["name"].Value;
                string type = attributes["type"].Value;

                description = Types.FromTypeName(type);
                description.Name = name;
                if (attributes["fkey"] != null)
                {
                    string reference = attributes["fkey"].Value;
                    if (unify)
                    {
                        reference = UnifyName(reference);
                    }
                    description.ForeignReference = reference;
                }
                if (attributes["pk"] != null)
                {
                    description.PrimaryKey = true;
                }

                ListType list = description as ListType;
                if (list != null)
                {
                    FillFieldList(list.Infos, fieldNode.ChildNodes, unify);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw e;
            }
            return description;
        }
    }

    public class XmlExporter
    {
        TextWriter writer;

        public bool LogWriting
        {
            get; set;
        }

        public XmlExporter(Stream stream)
        {
            writer = new StreamWriter(stream);
        }

        /*
         * Collect all GUIDs with the same type name and definition structure to store them in a single entry.
         */
        private List<TypeInfo> CompileSameDefinitions(List<TypeInfo> sourceList)
        {
            Dictionary<string, List<TypeInfo>> typeMap = new Dictionary<string, List<TypeInfo>>();

            foreach (TypeInfo typeInfo in sourceList)
            {
                if (!typeMap.ContainsKey(typeInfo.Name))
                {
                    List<TypeInfo> addTo = new List<TypeInfo>();
                    addTo.Add(typeInfo);
                    typeMap.Add(typeInfo.Name, addTo);
                }
                else
                {
                    bool added = false;
                    foreach (TypeInfo existing in typeMap[typeInfo.Name])
                    {
                        if (Enumerable.SequenceEqual<FieldInfo>(typeInfo.Fields, existing.Fields))
                        {
                            added = true;
                            break;
                        }
                    }
                    if (!added)
                    {
                        typeMap[typeInfo.Name].Add(typeInfo);
                    }
                }
            }
            List<TypeInfo> result = new List<TypeInfo>();
            foreach (List<TypeInfo> infos in typeMap.Values)
            {
                result.AddRange(infos);
            }
            return result;
        }
    }

    #region Formatting
    abstract class TableInfoFormatter<T>
    {
        public abstract string FormatHeader(T toWrite);
        public string FormatField(FieldInfo description)
        {
            StringBuilder builder = new StringBuilder("    <field ");
            builder.Append(FormatFieldContent(description));
            builder.Append("/>");
            return builder.ToString();
        }
        public virtual string FormatFieldContent(FieldInfo description)
        {
            StringBuilder builder = new StringBuilder();
            if (!description.ForeignReference.Equals(""))
            {
                builder.Append(string.Format("fkey='{0}' ", description.ForeignReference));
            }
            builder.Append(string.Format("name='{0}' ", description.Name));
            builder.Append(string.Format("type='{0}' ", description.TypeName));
            if (description.PrimaryKey)
            {
                builder.Append("pk='true' ");
            }
            return builder.ToString();
        }
    }
    /*
     * Formats header with tablename/version and list of applicable GUIDs.
     */
    class GuidTableInfoFormatter : TableInfoFormatter<TypeInfo>
    {
        static string HEADER_FORMAT = "  <table table_name='{0}'" + Environment.NewLine +
            "         table_version='{1}' >";

        public override string FormatHeader(TypeInfo info)
        {
            return string.Format(HEADER_FORMAT, info.Name, info.Version);
        }
    }
    #endregion
}