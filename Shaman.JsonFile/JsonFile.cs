﻿using Newtonsoft.Json;
#if !STANDALONE
using Shaman.Rest;
using Shaman.Types;
#endif
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if !CORECLR
using System.Windows.Forms;
#endif
// using JsonTypeDefinition = Xamasoft.JsonClassGenerator.JsonType;
// using JsonTypeEnum = Xamasoft.JsonClassGenerator.JsonTypeEnum;

#if SMALL_LIB_AWDEE
namespace Shaman.Runtime
#else
namespace Xamasoft
#endif
{

#if !STANDALONE
    [RestrictedAccess]
#endif
    public static class JsonFile
    {
        static JsonFile()
        {
        }


        public enum Format
        {
            FormattedJson = 0,
            CompactJson = 1,
#if BSON
            Bson = 2,
#endif
#if PROTOBUF
            Protobuf = 3,
#endif
            Automatic = 4
        }

        [StaticFieldCategory(StaticFieldCategory.TODO)]
        internal static readonly ConcurrentDictionary<string, JsonFileInternal> OpenFiles = new ConcurrentDictionary<string, JsonFileInternal>();

        [StaticFieldCategory(StaticFieldCategory.Stable)]
        private static bool basePathLoaded;
        [StaticFieldCategory(StaticFieldCategory.Stable)]
        private static string basePath;

        [Configuration]
        public readonly static string Configuration_BasePath = null;

        public static string BasePath
        {
            get
            {
                if (!basePathLoaded)
                {
#if STANDALONE
                    var entrypointDirectory = Directory.GetCurrentDirectory();
#else
                    var entrypointDirectory = ConfigurationManager.EntrypointDirectory;
#endif
                    if (Configuration_BasePath != null)
                    {
                        basePath = Path.Combine(entrypointDirectory, Configuration_BasePath);
                    }
                    else
                    {
                        var folderName = Path.GetFileName(entrypointDirectory);
                        basePath =
                            folderName.Equals("Release", StringComparison.OrdinalIgnoreCase) ||
                            folderName.Equals("Debug", StringComparison.OrdinalIgnoreCase)
                                ? Path.Combine(entrypointDirectory, @"../..")
                                : entrypointDirectory;
                    }
                    basePathLoaded = true;
                }
                return basePath;
            }
            set
            {
                if (value != null) Directory.CreateDirectory(value);
                basePath = value;
                basePathLoaded = true;
            }
        }


        public static JsonFile<T> Open<T>(string path)
        {
            return Open<T>(path, Format.Automatic);
        }

        public static JsonFile<T> Open<T>(string path, Format format)
        {
            path = GetPath(path);
            var normalized = Path.GetFullPath(path).ToLowerFast();
            if (format == Format.Automatic) format = GetFormatFromExtension(path);
            var file = (JsonFileInternal<T>)OpenFiles.GetOrAdd(normalized, dummy => new JsonFileInternal<T>(path, normalized, format));
            if (file.threadId != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Attempt to open an already open JsonFile on a different thread.");
            file.AcquireReference();
            return new JsonFile<T>(file);
        }

        public static JsonFile<List<T>> OpenList<T>(string path)
        {
            return OpenList<T>(path, Format.Automatic);
        }
        public static JsonFile<List<T>> OpenList<T>(string path, Format format)
        {
            return Open<List<T>>(path, format);
        }

        private const Format DEFAULT_JSON_FORMAT = Format.FormattedJson;

        public static JsonFile<T> Open<T>()
        {
            return Open<T>(GetPath(typeof(T), DEFAULT_JSON_FORMAT), DEFAULT_JSON_FORMAT);
        }
        public static JsonFile<List<T>> OpenList<T>()
        {
            return Open<List<T>>(GetPath(typeof(List<T>), DEFAULT_JSON_FORMAT), DEFAULT_JSON_FORMAT);
        }

        private static string GetPath(Type type, Format format)
        {
            var ext = ".json";
#if BSON
            if (format == Format.BSON) ext = ".bson";
#endif
#if PROTOBUF
            if (format == Format.Protobuf) ext = ".pb";
#endif
            return GetPath(GetFriendlyTypeName(type) + ext);
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type.GetTypeInfo().IsGenericType)
            {
                var generic = type.GetGenericTypeDefinition();
                var name = generic.Namespace == "System.Collections.Generic" ? generic.Name : generic.FullName;
                var idx = name.LastIndexOf('`');
                if (idx != -1) name = name.Substring(0, idx);
                return name + "(" + string.Join(",", type.GetGenericArguments().Select(x => GetFriendlyTypeName(x))) + ")";
            }
            return type.FullName;
        }

        private static string GetPath(string path)
        {
            if (BasePath != null && !Path.IsPathRooted(path)) path = Path.Combine(BasePath, path);
            return System.IO.Path.GetFullPath(path);
        }


        public static void Save(object obj)
        {
            Save(obj, GetPath(obj.GetType(), DEFAULT_JSON_FORMAT), DEFAULT_JSON_FORMAT);
        }

        private static Format GetFormatFromExtension(string path)
        {
            var ext = Path.GetExtension(path);
            if (ext == null) return DEFAULT_JSON_FORMAT;
#if BSON
            if (ext.Equals(".bson")) return Format.Bson;
#endif
#if PROTOBUF
            if (ext.Equals(".pb")) return Format.Protobuf;
#endif
            return DEFAULT_JSON_FORMAT;
        }

        public static void Save(object obj, string path)
        {
            Save(obj, path, GetFormatFromExtension(path));
        }

#if PROTOBUF
        [StaticFieldCategory(StaticFieldCategory.Stable)]
        private static MethodInfo _protobufSerializeOfTMethod;
        internal static MethodInfo ProtobufSerializeOfTMethod => _protobufSerializeOfTMethod ?? (_protobufSerializeOfTMethod = typeof(ProtoBuf.Serializer).GetRuntimeMethods().Single(x => x.Name == "Serialize" && x.GetParameters().Length == 2 && x.GetParameters()[0].ParameterType == typeof(Stream)));
#endif

        public static void Save(object obj, string path, Format format)
        {
            path = GetPath(path);
            var tempPath = Path.Combine(Path.GetDirectoryName(path), "$" + Path.GetFileName(path) + ".tmp");
#if BSON
            if (format == Format.Bson)
            {
                throw Sanity.NotImplementedException();
            }
#endif

#if PROTOBUF
            if (format == Format.Protobuf)
            {
                path = GetPath(path);
                using (var stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.Delete))
                {
                    ProtobufSerializeOfTMethod.MakeGenericMethodFast(obj.GetType()).Invoke(null, new[] { stream, obj });
                }
                File.Delete(path);
                File.Move(tempPath, path);
                return;
            }
#endif



            {
                path = GetPath(path);
                using (var stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.Delete))
                using (var sw = new StreamWriter(stream, Encoding.UTF8))
                {
                    var js = new JsonSerializer();
#if !STANDALONE
                    js.AddAwdeeConverters();
#endif
                    js.Formatting = format == Format.CompactJson ? Formatting.None : Formatting.Indented;
                    js.Serialize(sw, obj);
                }
                File.Delete(path);
                File.Move(tempPath, path);
                return;
            }


        }

    }

    public class JsonFile<T> : IDisposable
    {
        private JsonFileInternal<T> data;
        internal JsonFile(JsonFileInternal<T> data)
        {
            this.data = data;
            this.MaximumUncommittedChanges = 100;
        }

        private long changeCount = 0;

        public void DiscardAll()
        {
            this.data.DiscardAll();
        }

        public long MaximumUncommittedChanges { get; set; }
        public long ChangeCount { get { return changeCount; } }

        public void IncrementChangeCount()
        {
            changeCount++;
        }

        public void IncrementChangeCountAndMaybeSave()
        {
            changeCount++;
            MaybeSave();
        }

        public void MaybeSave()
        {
            if (changeCount >= MaximumUncommittedChanges)
                this.Save();
        }

        public T Content
        {
            get
            {
                if (data == null) throw new ObjectDisposedException("JsonFile");
                return data.Content;
            }
            set
            {
                if (data == null) throw new ObjectDisposedException("JsonFile");
                data.Content = value;
            }
        }

        public void Dispose()
        {

            if (data != null)
            {
                if (Environment.CurrentManagedThreadId != data.threadId) throw new InvalidOperationException("Attempt to dispose a JsonFile from a wrong thread.");

                data.ReleaseReference();
                data = null;
            }
        }

        public void Save()
        {
            changeCount = 0;
            data.Save();
        }

        public void MigrateToFormat(JsonFile.Format destinationFormat)
        {
            data.MigrateToFormat(destinationFormat);
        }
    }

    internal abstract class JsonFileInternal
    {


        public abstract void AcquireReference();
        public abstract void ReleaseReference();


    }

    internal class JsonFileInternal<T> : JsonFileInternal
    {

        private int usageCount;
        private string key;
        private volatile string path;
        private string tempPath;
        private string transactionFile;
        private string lastWrittenJson;
        internal readonly int threadId;

        private T content;

        private JsonFile.Format format;

        internal JsonFileInternal(string path, string key, JsonFile.Format format)
        {
            this.threadId = Environment.CurrentManagedThreadId;
            this.format = format;
            this.key = key;
            this.path = path;
            this.tempPath = Path.Combine(Path.GetDirectoryName(path), "$" + Path.GetFileName(path) + ".tmp");
            this.transactionFile = path + ".transaction";
            if (File.Exists(transactionFile))
            {
                File.Move(transactionFile, path);
            }

            if (File.Exists(path))
            {
#if BSON
                if (format == JsonFile.Format.Bson)
                {

                    using (var stream = File.OpenRead(path))
                    using (var reader = new BsonReader(stream))
                    {
                        var deserializer = new JsonSerializer();
                        this.content = deserializer.Deserialize<T>(reader);
                    }
                }
                else
#endif
#if PROTOBUF
                if (format == JsonFile.Format.Protobuf)
                {
                    using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Delete | FileShare.Read))
                    {
                        this.content = ProtoBuf.Serializer.Deserialize<T>(stream);
                    }

                }
                else
#endif
                {

                    this.lastWrittenJson = File.ReadAllText(path);
                    this.content = JsonConvert.DeserializeObject<T>(this.lastWrittenJson);
                }
            }
            else
            {
                this.content = Activator.CreateInstance<T>();
                Save(true);
            }
        }

        public T Content
        {
            get
            {
                if (path == null) throw new ObjectDisposedException("JsonFile");
                return content;
            }
            set
            {
                if (path == null) throw new ObjectDisposedException("JsonFile");
                this.content = value;
            }
        }

        public override void AcquireReference()
        {
            usageCount++;
        }

        internal void Save(bool isNew = false)
        {
            if (path == null) throw new InvalidOperationException();
            bool shouldSave = false;

#if BSON
            if (format == JsonFile.Format.Bson)
            {
                using (var stream = File.OpenWrite(tempPath))
                using (var writer = new BsonWriter(stream))
                {
                    var s = new JsonSerializer();
                    s.Serialize(writer, content);
                }
            }
            else
            
#endif
#if PROTOBUF
            if (format == JsonFile.Format.Protobuf)
            {
                using (var stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.Delete))
                {
                    ProtoBuf.Serializer.Serialize(stream, content);
                }
                shouldSave = true;
            }
            else
#endif
            if (format == JsonFile.Format.CompactJson || format == JsonFile.Format.FormattedJson)
            {
                var json = JsonConvert.SerializeObject(content, format == JsonFile.Format.FormattedJson ? Formatting.Indented : Formatting.None);
                if (json != lastWrittenJson)
                {

                    File.WriteAllText(tempPath, json, Encoding.UTF8);
                    lastWrittenJson = json;
                    shouldSave = true;
                }
            }
            else
            {
#if STANDALONE
                throw new ArgumentException();
#else
                Sanity.ShouldntHaveHappened();
#endif
            }

            if (shouldSave)
            {
                if (isNew)
                {
                    File.Move(tempPath, path);
                }
                else
                {
                    File.Move(path, transactionFile);
                    File.Move(tempPath, path);
                    File.Delete(transactionFile);
                }
            }

        }

        public override void ReleaseReference()
        {
            if (--usageCount == 0)
            {
                if (path != null) Save();
                this.path = null;
                JsonFileInternal dummy;
                JsonFile.OpenFiles.TryRemove(key, out dummy);
                content = default(T);
            }

        }

        public void DiscardAll()
        {
            this.path = null;
            JsonFileInternal dummy;
            JsonFile.OpenFiles.TryRemove(key, out dummy);
            content = default(T);
        }


        public void MigrateToFormat(JsonFile.Format destinationFormat)
        {
            lastWrittenJson = null;
            format = destinationFormat;
            Save();
        }

    }

#if !STANDALONE
    [RestrictedAccess]
#endif
    public static class ReplExtensions
    {
#if !CORECLR
        public static string CopyText(this string text)
        {
            RunInSTA(() =>
            {
                if (string.IsNullOrEmpty(text)) Clipboard.Clear();
                Clipboard.SetText(text, System.Windows.Forms.TextDataFormat.UnicodeText);
            });
            return text;
        }

        public static string PasteAsText()
        {
            string content = null;
            RunInSTA(() => { content = Clipboard.GetText(); });
            return content;
        }
#endif

        public static void ViewJson(this object obj)
        {
            Console.WriteLine(obj.ToJson());
        }


        public static string ToJson(this object obj)
        {
            return JsonConvert.SerializeObject(obj, Formatting.Indented);
        }

        //public static object PasteAsJson()
        //{
        //    return DeserializeJson(PasteAsText());
        //}

        //private static int asmIndex;

        //private static ConstructorInfo JsonPropertyAttributeConstructor;

        //private static Type GetType(JsonTypeDefinition definition, ModuleBuilder moduleBuilder, Dictionary<JsonTypeDefinition, Type> existingTypes)
        //{
        //    switch (definition.Type)
        //    {
        //        case JsonTypeEnum.Anything: return typeof(object);
        //        case JsonTypeEnum.Array: return Array.CreateInstance(GetType(definition.InternalType, moduleBuilder, existingTypes), 0).GetType();
        //        case JsonTypeEnum.Boolean: return typeof(bool);
        //        case JsonTypeEnum.Date: return typeof(DateTime);
        //        case JsonTypeEnum.Dictionary: throw Sanity.NotImplemented();
        //        case JsonTypeEnum.Float: return typeof(float);
        //        case JsonTypeEnum.Integer: return typeof(int);
        //        case JsonTypeEnum.Long: return typeof(long);
        //        case JsonTypeEnum.NonConstrained: return typeof(object);
        //        case JsonTypeEnum.NullableBoolean: return typeof(bool?);
        //        case JsonTypeEnum.NullableDate: return typeof(DateTime?);
        //        case JsonTypeEnum.NullableFloat: return typeof(float?);
        //        case JsonTypeEnum.NullableInteger: return typeof(int?);
        //        case JsonTypeEnum.NullableLong: return typeof(long?);
        //        case JsonTypeEnum.NullableSomething: return typeof(object);
        //        case JsonTypeEnum.Object: break;
        //        case JsonTypeEnum.String: return typeof(string);
        //        default: throw new NotSupportedException();
        //    }
        //    if (existingTypes.ContainsKey(definition)) return existingTypes[definition];
        //    var builder = moduleBuilder.DefineType(definition.AssignedName);

        //    foreach (var field in definition.Fields)
        //    {
        //        var fieldBuilder = builder.DefineField(field.MemberName, GetType(field.Type, moduleBuilder, existingTypes), FieldAttributes.Public);
        //        var attrBuilder = new CustomAttributeBuilder(JsonPropertyAttributeConstructor, new object[] { field.JsonMemberName });
        //        fieldBuilder.SetCustomAttribute(attrBuilder);
        //    }
        //    return builder.CreateType();
        //}

        //public static object DeserializeJson(string json)
        //{
        //    if (JsonPropertyAttributeConstructor == null)
        //    {
        //        JsonPropertyAttributeConstructor = typeof(JsonPropertyAttribute).GetConstructor(new[] { typeof(string) });
        //    }


        //    var gen = new JsonClassGenerator.JsonClassGenerator();
        //    gen.CodeWriter = new JsonClassGenerator.CodeWriters.CSharpCodeWriter();
        //    gen.Example = json;
        //    var name = "Json" + Interlocked.Increment(ref asmIndex);
        //    gen.MainClass = name;
        //    var sw = new StringWriter();
        //    gen.OutputStream = sw;
        //    gen.UseNestedClasses = true;
        //    gen.UsePascalCase = true;
        //    gen.UseProperties = true;
        //    gen.GenerateClasses();

        //    var asmb = Thread.GetDomain().DefineDynamicAssembly(new AssemblyName(name), System.Reflection.Emit.AssemblyBuilderAccess.RunAndCollect);
        //    var modb = asmb.DefineDynamicModule(name);
        //    var type = GetType(gen.Types.Single(x => x.IsRoot), modb, new Dictionary<JsonTypeDefinition, Type>());
        //    return JsonConvert.DeserializeObject(json, type);
        //}

#if !CORECLR
        private static void RunInSTA(Action action)
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                var s = new Thread(() => action());
                s.Name = "RunInSTA";
                s.SetApartmentState(ApartmentState.STA);
                s.Start();
                s.Join();
                return;
            }
            action();
        }

        public static T CopyJson<T>(this T obj)
        {
            CopyText(ToJson(obj));
            return obj;
        }
        public static IEnumerable CopyTable(this IEnumerable items)
        {
            CopyText(ToTable(items));
            return items;
        }
        public static IEnumerable<T> CopyTable<T>(this IEnumerable<T> items)
        {
            CopyText(ToTable(items));
            return items;
        }

        public static IEnumerable CopyTable<T>(this IEnumerable items, string caption)
        {
            CopyText(caption + "\n" + ToTable(items));
            return items;
        }
        public static IEnumerable<T> CopyTable<T>(this IEnumerable<T> items, string caption)
        {
            CopyText(caption + "\n" + ToTable(items));
            return items;
        }
#endif
        public static IEnumerable<T> SaveTable<T>(this IEnumerable<T> items, string fileName)
        {
            File.WriteAllText(fileName, ToTable(items), Encoding.UTF8);
            return items;
        }

        public static void ViewTable(this IEnumerable items)
        {
            Console.WriteLine(ToTable(items));
        }

        public static void ViewTable<T>(this IEnumerable<T> items)
        {
            Console.WriteLine(ToTable(items));
        }

        private class Field
        {
            public string Name;
            public Func<object, object> Get;
            public Type Type;
        }

        private static List<Field> GetFields(Type type)
        {
#if !STANDALONE
            if (type.Is<Entity>())
            {
                return EntityType.FromNativeType(type).Fields.Select(x => new Field()
                {
                    Name = x.Name,
                    Get = new Func<object, object>(y =>
                    {
                        var ent = (Entity)y;
                        return x.IsStoredIn(ent) ? x.GetStoredValueDirect(ent) : null;
                    }),
                    Type = x.FieldType.NativeType
                }).ToList();
            };
#endif
            var emptyArray = new object[] { };
            var fields =
            type.GetTypeInfo().IsPrimitive || type.GetTypeInfo().IsEnum || type == typeof(string) ? new[] { new Field { Name = "Value", Get = new Func<object, object>(y => y), Type = type } }.ToList() :
            type.GetFields(BindingFlags.Instance | BindingFlags.Public)
#if !STANDALONE
            .Where(x => x.GetCustomAttribute<RestrictedAccessAttribute>() == null)
#endif
            .Select(x => new Field { Name = x.Name, Get = new Func<object, object>(y => x.GetValue(y)), Type = x.FieldType })
            .Union(type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(x =>
                 x.GetIndexParameters().Length == 0
#if !STANDALONE
                 && x.DeclaringType != typeof(Entity)
                 && x.GetMethod.GetCustomAttribute<RestrictedAccessAttribute>() == null
#endif
)
             .Select(x => new Field { Name = x.Name, Get = new Func<object, object>(y => x.GetValue(y, emptyArray)), Type = x.PropertyType }))
            .ToList();
            return fields;

        }


        public static string ToTable<T>(this IEnumerable<T> items)
        {
            using (var sw = new StringWriter())
            {
                ToTable(items, typeof(T), sw, '\t');
                return sw.ToString();
            }
        }

        public static string ToTable(this IEnumerable items)
        {
            using (var sw = new StringWriter())
            {
                ToTable(items, GetEnumerableElementType(items), sw, '\t');
                return sw.ToString();
            }
        }

        public static void ToTable(IEnumerable items, Type type, TextWriter tw, char separator)
        {

            var fields = GetFields(type);
            var first = true;
            foreach (var field in fields)
            {
                if (!first) tw.Write(separator);
                tw.Write(field.Name);
                first = false;
            }
            tw.Write('\r');
            tw.Write('\n');
            foreach (var item in items)
            {
                first = true;
                foreach (var field in fields)
                {

                    if (!first) tw.Write(separator);
                    var val = field.Get(item);
#if !STANDALONE
                    var es = val as EntitySet;
                    if (es != null) val = Utils.GetRestPath(es);
#endif
                    tw.Write(RemoveForbiddenChars(val, separator));
                    first = false;
                }
                tw.Write('\r');
                tw.Write('\n');
            }
        }

#if !STANDALONE
        [RestrictedAccess]
#endif
        public static IEnumerable<T> OpenExcel<T>(this IEnumerable<T> items)
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
            SaveExcel(items, temp);
            Process.Start(temp);
            return items;
        }

        public static IEnumerable OpenExcel<T>(this IEnumerable items)
        {
            var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
            SaveExcel(items, temp);
            Process.Start(temp);
            return items;
        }




        public static IEnumerable<T> SaveExcel<T>(this IEnumerable<T> items, string xlsx)
        {
            var f = new OfficeOpenXml.ExcelPackage();
            var sheet = f.Workbook.Worksheets.Add("Sheet1");
            SaveExcel(items, sheet);
            f.SaveAs(new FileInfo(xlsx));
            return items;
        }


        public static IEnumerable SaveExcel(this IEnumerable items, string xlsx)
        {
            var f = new OfficeOpenXml.ExcelPackage();
            var sheet = f.Workbook.Worksheets.Add("Sheet1");
            SaveExcel(items, sheet, GetEnumerableElementType(items), null);
            f.SaveAs(new FileInfo(xlsx));
            return items;
        }

        private static Type GetEnumerableElementType(object ienumerable)
        {
            return ienumerable.GetType().GetInterfaces().First(x => x.GetTypeInfo().IsGenericType && x.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GetGenericArguments()[0];

        }



        public static IEnumerable<T> SaveExcel<T>(this IEnumerable<T> items, OfficeOpenXml.ExcelWorksheet sheet)
        {
            SaveExcel(items, sheet, typeof(T), null);
            return items;
        }

        //internal static object Unexpand(object item)
        //{
        //    var exp = item as ExpandedEntity;
        //    if (exp != null) return exp.Entity;
        //    return item;
        //}

        internal static readonly Type[] NumericTypes = new[]{
           typeof(Byte),typeof(SByte),
           typeof(Int16),typeof(Int32),typeof(Int64),
           typeof(UInt16),typeof(UInt32),typeof(UInt64),
           typeof(Single),typeof(Double),typeof(Decimal)
        };

        public static void SaveExcel(IEnumerable items, OfficeOpenXml.ExcelWorksheet sheet, Type elementType, string schemeAndAuthorityBase)
        {

            //var table = sheet.Tables.Single();




            var headerRow = sheet.Row(1);
            var fields = GetFields(elementType);

            var nextRow = 2;

            var col = 1;
            foreach (var field in fields)
            {
                var cell = sheet.Cells[1, col];
                cell.Value = field.Name;
                col++;
                var ft = field.Type;
                var ti = ft.GetTypeInfo();
                if (ti.IsGenericType && ft.GetGenericTypeDefinition() == typeof(Nullable<>)) ft = ft.GetGenericArguments()[0];
                sheet.Column(col - 1).Width = ti.IsPrimitive || NumericTypes.Contains(ft) || ti.IsEnum || ft == typeof(DateTime) || ft == typeof(DateTimeOffset) ? 10 : 25;
            }

            foreach (var item in items)
            {
                var row = nextRow;
                nextRow++;

                col = 1;
                foreach (var prop in fields)
                {

                    var cell = sheet.Cells[row, col];
                    var val = prop.Get(item);
#if !STANDALONE
                    var es = val as EntitySet;
                    var ent = val as Entity;
                    if (es != null)
                    {
                        if (schemeAndAuthorityBase != null)
                        {
                            var url = (schemeAndAuthorityBase + Utils.GetRestPath(es)).AsUri();
                            cell.Hyperlink = url;
                            cell.Value = url;
                        }
                        else
                        {
                            cell.Value = "(Collection)";
                        }
                    }
                    else if (ent != null)
                    {
                        if (schemeAndAuthorityBase != null)
                        {
                            var url = (schemeAndAuthorityBase + Utils.GetRestPath(ent)).AsUri();
                            cell.Hyperlink = url;
                        }
                        cell.Value = ent.ToStringOrIdOrDefault(ent.EntityType.Name);
                    }
                    else
#endif
                    if (val is Uri)
                    {
                        var url = (Uri)val;
                        cell.Hyperlink = url;
                        cell.Value = url.AbsoluteUri;
                        //cell.Style.sy
                    }
#if !STANDALONE
                    else if (val is Money)
                    {
                        var money = (Money)val;
                        cell.Value = money.CurrencyValue;
                        if (money.CurrencyType != Currency.None)
                            cell.Style.Numberformat.Format = Money.GetExcelFormat(money.CurrencyType);
                    }
#endif
                    else
                    {
                        cell.Value = val;
                        if (val is DateTime || val is DateTimeOffset)
                        {
                            cell.Style.Numberformat.Format = "yyyy-mm-dd HH hh:mm";
                        }

                    }

                    col++;
                }
            }
            var t = sheet.Tables.Add(new OfficeOpenXml.ExcelAddressBase(1, 1, nextRow - 1, col - 1), sheet.Name + "-Items");
            t.TableStyle = OfficeOpenXml.Table.TableStyles.None;
        }



        public static string ToMatrix<T, TRow, TColumn, TCell>(this IEnumerable<T> items, Func<T, TRow> getRow, Func<T, TColumn> getColumn, Func<T, TCell> getCell)
        {
            var matrix = new Dictionary<TRow, Dictionary<TColumn, TCell>>();
            var allColumns = new HashSet<TColumn>();

            foreach (var item in items)
            {
                var row = getRow(item);
                var col = getColumn(item);
                var value = getCell(item);

                allColumns.Add(col);

                if (!matrix.ContainsKey(row)) matrix[row] = new Dictionary<TColumn, TCell>();

                matrix[row].Add(col, value);
            }

            var theColumns = allColumns.OrderBy(x => x).ToList();
            var theRows = matrix.Keys.OrderBy(x => x).ToList();

            var sb = ReseekableStringBuilder.AcquirePooledStringBuilder();

            foreach (var col in theColumns)
            {
                sb.Append('\t');
                sb.Append(col);
            }
            sb.AppendLine();
            foreach (var row in theRows)
            {
                sb.Append(row);
                var rowData = matrix[row];
                foreach (var col in theColumns)
                {
                    sb.Append('\t');
                    TCell value;
                    if (rowData.TryGetValue(col, out value))
                        sb.Append(RemoveForbiddenChars(value, '\t'));
                }
                sb.AppendLine();
            }
            return ReseekableStringBuilder.GetValueAndRelease(sb);
        }
        public static void ViewMatrix<T, TRow, TColumn, TCell>(this IEnumerable<T> items, Func<T, TRow> getRow, Func<T, TColumn> getColumn, Func<T, TCell> getCell)
        {
            Console.WriteLine(ToMatrix(items, getRow, getColumn, getCell));
        }
#if !CORECLR
        public static IEnumerable<T> CopyMatrix<T, TRow, TColumn, TCell>(this IEnumerable<T> items, Func<T, TRow> getRow, Func<T, TColumn> getColumn, Func<T, TCell> getCell)
        {
            CopyText(ToMatrix(items, getRow, getColumn, getCell));
            return items;
        }
        public static IEnumerable<T> CopyMatrix<T, TRow, TColumn, TCell>(this IEnumerable<T> items, Func<T, TRow> getRow, Func<T, TColumn> getColumn, Func<T, TCell> getCell, string caption)
        {
            CopyText(caption + "\n" + ToMatrix(items, getRow, getColumn, getCell));
            return items;
        }
#endif

        public static double RatioOf<T>(this IEnumerable<T> source, Func<T, bool> evalutor)
        {
            double sum = 0;
            double count = 0;
            foreach (var item in source)
            {
                if (evalutor(item))
                    sum++;
                count++;
            }
            return sum / count;
        }
        public static string RemoveForbiddenChars(object obj, char separator)
        {
            if (obj == null) return null;
            if (obj is DateTimeOffset)
            {
                obj = new DateTime(((DateTimeOffset)obj).Ticks);
            }
            if (obj is DateTime)
            {
                var d = (DateTime)obj;
                if (d.Ticks < TimeSpan.FromDays(2).Ticks) return null;
                if (d.TimeOfDay == TimeSpan.Zero) return d.ToString("yyyy-MM-dd");
                return d.ToString("yyyy-MM-dd HH:mm:ss");
            }
#if STANDALONE
            var str = Convert.ToString(obj, CultureInfo.InvariantCulture);
#else
            var str = Conversions.ConvertToDisplayString(obj);
#endif
            //var str = Convert.ToString(obj, CultureInfo.InvariantCulture);
            if (str == null) return null;
            return str.Replace("\r", "").Replace('\n', ' ').Replace(separator, ' ');
        }

    }


}
