// PROTOTYPE for ReparoHome#48. For each type a UiPath author might declare as a Variable: does
// value -> JSON (Newtonsoft, as UiPath itself uses) -> value of the DECLARED type give the same value back?
// The JSON goes through text, as on the wire. "same" = the re-serialised JSON matches, the runtime type matches, and for DataTable the column types match.
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

enum Priority { Low, High }
class Poco { public int Id { get; set; } public string Name { get; set; } public List<string> Tags { get; set; } }
class PrivateState { int secret = 7; public int Seen => secret; public PrivateState() { } public PrivateState(int s) { secret = s; } }

static class P
{
    static readonly JsonSerializer S = JsonSerializer.CreateDefault();

    /// The Bridge's parse settings: decimals stay exact, date strings stay strings until the declared type converts them.
    static bool Tuned;
    static JToken ParseTuned(string json)
    {
        using var r = new JsonTextReader(new System.IO.StringReader(json)) { FloatParseHandling = FloatParseHandling.Decimal, DateParseHandling = DateParseHandling.None };
        return JToken.ReadFrom(r);
    }

    static DataTable Table()
    {
        var t = new DataTable("decisions");
        t.Columns.Add("request_id", typeof(int)); t.Columns.Add("decision", typeof(string));
        t.Columns.Add("amount", typeof(decimal)); t.Columns.Add("at", typeof(DateTime));
        t.Rows.Add(101, "approved", 400.50m, new DateTime(2026, 11, 24, 10, 0, 0));
        t.Rows.Add(102, null, 800m, DBNull.Value);
        return t;
    }

    static string Cols(DataTable t) => string.Join(",", t.Columns.Cast<DataColumn>().Select(c => c.ColumnName + ":" + c.DataType.Name));

    /// Restore a DataTable into the schema of a live table, the way the Bridge could: columns from the live value, rows from JSON.
    static DataTable IntoSchema(JToken json, DataTable schema)
    {
        var t = schema.Clone();
        foreach (JObject row in json)
        {
            var r = t.NewRow();
            foreach (DataColumn c in t.Columns)
                r[c] = row.TryGetValue(c.ColumnName, out var v) && v.Type != JTokenType.Null ? v.ToObject(c.DataType) : DBNull.Value;
            t.Rows.Add(r);
        }
        return t;
    }

    static void Try(string label, Type declared, Func<object> make)
    {
        string verdict, json = "";
        try
        {
            var value = make();
            var tok = value == null ? JValue.CreateNull() : JToken.FromObject(value, S);
            json = tok.ToString(Formatting.None);
            // As on the wire: JSON text goes to Reparo and JSON text comes back.
            var back = (Tuned ? ParseTuned(json) : JToken.Parse(json)).ToObject(declared, S);
            var json2 = back == null ? "null" : JToken.FromObject(back, S).ToString(Formatting.None);
            var notes = new List<string>();
            if (json2 != json) notes.Add("JSON differs after round trip: " + Cut(json2));
            if (value != null && back != null && value.GetType() != back.GetType()) notes.Add($"comes back as {back.GetType().Name}, was {value.GetType().Name}");
            if (value is DataTable a && back is DataTable b && Cols(a) != Cols(b)) notes.Add($"columns {Cols(a)} -> {Cols(b)}");
            if (value is SecureString ss && back is SecureString bs && ss.Length != bs.Length) notes.Add($"length {ss.Length} -> {bs.Length}");
            if (value is PrivateState ps && back is PrivateState pb && ps.Seen != pb.Seen) notes.Add($"private field {ps.Seen} -> {pb.Seen}");
            if (value is Image im && back is Image ib && im.Size != ib.Size) notes.Add("image differs");
            verdict = notes.Count == 0 ? "OK" : "LOSSY: " + string.Join("; ", notes);
        }
        catch (Exception e) { verdict = "FAILS: " + e.GetType().Name + ": " + e.Message.Split('\n')[0]; }
        Console.WriteLine($"{label,-44} {verdict}");
        Console.WriteLine($"{"",-44}   json: {Cut(json)}");
    }

    static string Cut(string s) => s.Length > 160 ? s.Substring(0, 160) + "..." : s;

    static void Main()
    {
        foreach (var tuned in new[] { false, true }) { Tuned = tuned; Console.WriteLine(); Console.WriteLine(tuned ? "=== TUNED parse (FloatParseHandling.Decimal, DateParseHandling.None)" : "=== DEFAULT parse"); All(); }
    }

    static void All()
    {
        Console.WriteLine("Newtonsoft " + typeof(JsonConvert).Assembly.GetName().Version + ", .NET " + Environment.Version);
        Try("String", typeof(string), () => "héllo \"q\"");
        Try("Int32", typeof(int), () => 42);
        Try("Int64", typeof(long), () => long.MaxValue);
        Try("Double", typeof(double), () => 0.1 + 0.2);
        Try("Double NaN", typeof(double), () => double.NaN);
        Try("Decimal", typeof(decimal), () => 12345678901234567890.123456789m);
        Try("Boolean", typeof(bool), () => true);
        Try("Char", typeof(char), () => 'x');
        Try("DateTime Local", typeof(DateTime), () => new DateTime(2026, 11, 24, 18, 30, 0, DateTimeKind.Local));
        Try("DateTime Utc", typeof(DateTime), () => new DateTime(2026, 11, 24, 18, 30, 0, DateTimeKind.Utc));
        Try("DateTime Unspecified", typeof(DateTime), () => new DateTime(2026, 11, 24, 18, 30, 0));
        Try("DateTimeOffset", typeof(DateTimeOffset), () => new DateTimeOffset(2026, 11, 24, 18, 30, 0, TimeSpan.FromHours(1)));
        Try("TimeSpan", typeof(TimeSpan), () => TimeSpan.FromMinutes(90.5));
        Try("Guid", typeof(Guid), () => Guid.NewGuid());
        Try("Nullable Int32 (Nothing)", typeof(int?), () => null);
        Try("Enum", typeof(Priority), () => Priority.High);
        Try("Uri", typeof(Uri), () => new Uri("https://example.com/a?b=1"));
        Try("Version", typeof(Version), () => new Version(1, 2, 3));
        Try("Byte[]", typeof(byte[]), () => new byte[] { 1, 2, 255 });
        Try("String[]", typeof(string[]), () => new[] { "a", "b" });
        Try("Int32[]", typeof(int[]), () => new[] { 1, 2 });
        Try("List(Of Int32)", typeof(List<int>), () => new List<int> { 101, 104 });
        Try("List(Of String)", typeof(List<string>), () => new List<string> { "amount", "budget" });
        Try("HashSet(Of String)", typeof(HashSet<string>), () => new HashSet<string> { "x" });
        Try("Queue(Of Int32)", typeof(Queue<int>), () => new Queue<int>(new[] { 1, 2 }));
        Try("Dictionary(Of String, Int32)", typeof(Dictionary<string, int>), () => new Dictionary<string, int> { ["amount"] = 400 });
        Try("Dictionary(Of String, Object) mixed", typeof(Dictionary<string, object>), () => new Dictionary<string, object> { ["n"] = 1, ["s"] = "x", ["d"] = new DateTime(2026, 1, 1) });
        Try("Dictionary(Of Int32, String)", typeof(Dictionary<int, string>), () => new Dictionary<int, string> { [1] = "a" });
        Try("KeyValuePair(Of String, Int32)", typeof(KeyValuePair<string, int>), () => new KeyValuePair<string, int>("k", 1));
        Try("Tuple(Of Int32, String)", typeof(Tuple<int, string>), () => Tuple.Create(1, "a"));
        Try("ValueTuple(Of Int32, String)", typeof(ValueTuple<int, string>), () => (1, "a"));
        Try("JObject", typeof(JObject), () => JObject.Parse("{\"a\":[1,{\"b\":null}]}"));
        Try("JArray", typeof(JArray), () => JArray.Parse("[1,\"x\"]"));
        Try("Object holding Int32", typeof(object), () => 42);
        Try("Object holding String", typeof(object), () => "x");
        Try("Object holding DataTable", typeof(object), () => Table());
        Try("DataTable (typed columns, DBNull)", typeof(DataTable), Table);
        Try("DataTable empty with columns", typeof(DataTable), () => Table().Clone());
        Try("DataRow", typeof(DataRow), () => Table().Rows[0]);
        Try("DataSet", typeof(DataSet), () => { var d = new DataSet("ds"); d.Tables.Add(Table()); return d; });
        Try("POCO class", typeof(Poco), () => new Poco { Id = 1, Name = "n", Tags = new() { "t" } });
        Try("Class with private state", typeof(PrivateState), () => new PrivateState(42));
        Try("SecureString", typeof(SecureString), () => new NetworkCredential("", "s3cret").SecurePassword);
        Try("NetworkCredential", typeof(NetworkCredential), () => new NetworkCredential("u", "p"));
        Try("MailMessage", typeof(MailMessage), () => new MailMessage("a@example.com", "b@example.com", "s", "b"));
        Try("Image (Bitmap)", typeof(Image), () => new Bitmap(4, 4));
        Try("Exception", typeof(Exception), () => new InvalidOperationException("boom"));
        Try("Process", typeof(System.Diagnostics.Process), () => System.Diagnostics.Process.GetCurrentProcess());

        Console.WriteLine();
        Console.WriteLine("DataTable restored INTO the live value's schema (the Bridge's own path):");
        var live = Table();
        var restored = IntoSchema((Tuned ? ParseTuned(JToken.FromObject(live, S).ToString()) : JToken.Parse(JToken.FromObject(live, S).ToString())), live);
        Console.WriteLine($"  columns {Cols(restored)}; rows {restored.Rows.Count}; row0 {string.Join("|", restored.Rows[0].ItemArray)}; row1 {string.Join("|", restored.Rows[1].ItemArray.Select(x => x is DBNull ? "DBNull" : x))}");
        var plain = JToken.Parse(JToken.FromObject(live, S).ToString()).ToObject<DataTable>(S);
        Console.WriteLine($"  plain Newtonsoft gives columns {Cols(plain)}");
    }
}
