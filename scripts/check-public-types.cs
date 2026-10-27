#:property PublishAot=false
// Reads .NET assemblies by their metadata only (nothing is loaded or run) and checks what they expose. Used by CI on the packed files.
//
//   dotnet run scripts/check-public-types.cs -- <path to dll> [allowed full type name ...]
//     Lists the public types and fails when one is not on the allowlist. Used on the packed Ageledger.IosBinding.dll, which is an
//     implementation detail of the package.
//
//   dotnet run scripts/check-public-types.cs -- --crefs <path to xml> <path to dll> [<path to dll> ...] [--allow-none]
//     Every cref in the XML documentation file that points into "Ageledger." must name a type or member that is public or protected
//     in the first of the given assemblies that has the type. A cref whose type is in none of them ("type not found") and an unresolved
//     cref (written "!:" by the compiler) also fail. A method cref matches overloads by parameter count (no parameter list means none). A file with no cref into the assemblies is an error, unless --allow-none says that is
//     expected (Ageledger.Testing.xml has none).
//
//   dotnet run scripts/check-public-types.cs -- --pdb-paths <pdb file or folder> [<pdb file or folder> ...]
//     Reads every portable PDB (a folder is searched for *.pdb, also in subfolders) and fails unless every source document name starts
//     with "/_/", the root that deterministic source paths map the repository to. A build-machine path in a PDB would not start with it.
//     No PDB found is an error.
//
// Exit code 0: all good. 1: a check failed. 2: usage or read error, or the input was the wrong file (almost no types, no cref to check).
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml;
using System.Xml.Linq;

if (args.Length > 0 && args[0] == "--crefs") return CheckCrefs(args.Skip(1).ToList());
if (args.Length > 0 && args[0] == "--pdb-paths") return CheckPdbPaths(args.Skip(1).ToList());

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: check-public-types <dll> [allowed full type name ...]  |  --crefs <xml> <dll> [<dll> ...] [--allow-none]");
    return 2;
}

var allowed = new HashSet<string>(args.Skip(1), StringComparer.Ordinal);
var publicTypes = new List<string>();
var totalTypes = 0;
try
{
    using var stream = File.OpenRead(args[0]);
    using var pe = new PEReader(stream);
    var md = pe.GetMetadataReader();
    foreach (var handle in md.TypeDefinitions)
    {
        totalTypes++;
        var def = md.GetTypeDefinition(handle);
        var visibility = def.Attributes & TypeAttributes.VisibilityMask;
        if (visibility is not (TypeAttributes.Public or TypeAttributes.NestedPublic)) continue;
        // A nested public type is only reachable when every type around it is public too.
        var exposed = true;
        for (var outer = def.GetDeclaringType(); !outer.IsNil && exposed; outer = md.GetTypeDefinition(outer).GetDeclaringType())
            exposed = (md.GetTypeDefinition(outer).Attributes & TypeAttributes.VisibilityMask) is TypeAttributes.Public or TypeAttributes.NestedPublic;
        if (!exposed) continue;
        publicTypes.Add(FullName(md, def, '+'));
    }
}
catch (Exception e) when (e is IOException or BadImageFormatException or UnauthorizedAccessException)
{
    Console.Error.WriteLine("cannot read " + args[0] + ": " + e.GetType().Name);
    return 2;
}

publicTypes.Sort(StringComparer.Ordinal);
Console.WriteLine($"{publicTypes.Count} public type(s) of {totalTypes} in {Path.GetFileName(args[0])}:");
// A binding with all its types internal still has them; an assembly with next to no types means the wrong file was read.
if (totalTypes < 5)
{
    Console.Error.WriteLine("the assembly has almost no types; is this the right file?");
    return 2;
}
foreach (var t in publicTypes) Console.WriteLine("  " + t + (allowed.Contains(t) ? "" : "   <-- not allowed"));
var unexpected = publicTypes.Where(t => !allowed.Contains(t)).ToList();
if (unexpected.Count > 0)
{
    Console.WriteLine($"::error::{Path.GetFileName(args[0])} has {unexpected.Count} public type(s) that are not on the allowlist.");
    return 1;
}
return 0;

static string FullName(MetadataReader md, TypeDefinition def, char nestedSeparator)
{
    var name = md.GetString(def.Name);
    var declaring = def.GetDeclaringType();
    if (!declaring.IsNil) return FullName(md, md.GetTypeDefinition(declaring), nestedSeparator) + nestedSeparator + name;
    var ns = md.GetString(def.Namespace);
    return ns.Length == 0 ? name : ns + "." + name;
}

static int CheckPdbPaths(List<string> inputs)
{
    if (inputs.Count < 1)
    {
        Console.Error.WriteLine("usage: --pdb-paths <pdb file or folder> [<pdb file or folder> ...]");
        return 2;
    }
    var files = new List<string>();
    foreach (var input in inputs)
    {
        if (File.Exists(input)) files.Add(input);
        else if (Directory.Exists(input)) files.AddRange(Directory.EnumerateFiles(input, "*.pdb", SearchOption.AllDirectories));
        else
        {
            Console.Error.WriteLine("not found: " + input);
            return 2;
        }
    }
    if (files.Count == 0)
    {
        Console.Error.WriteLine("no .pdb file was found, so this check would prove nothing.");
        return 2;
    }
    var bad = new List<string>();
    var documents = 0;
    foreach (var file in files.Order(StringComparer.Ordinal))
    {
        try
        {
            using var stream = File.OpenRead(file);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            var md = provider.GetMetadataReader();
            foreach (var h in md.Documents)
            {
                documents++;
                var name = md.GetString(md.GetDocument(h).Name);
                if (!name.StartsWith("/_/", StringComparison.Ordinal)) bad.Add(Path.GetFileName(file) + ": " + name);
            }
        }
        catch (Exception e) when (e is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("cannot read " + Path.GetFileName(file) + ": " + e.GetType().Name);
            return 2;
        }
    }
    Console.WriteLine($"{documents} document name(s) in {files.Count} PDB(s) checked.");
    if (documents == 0)
    {
        Console.Error.WriteLine("the PDBs list no source documents, so this check would prove nothing.");
        return 2;
    }
    foreach (var b in bad.Take(20)) Console.WriteLine("::error::A PDB document name does not start with /_/: " + b);
    if (bad.Count > 20) Console.WriteLine($"::error::... and {bad.Count - 20} more.");
    return bad.Count == 0 ? 0 : 1;
}

static int CheckCrefs(List<string> rest)
{
    var allowNone = false;
    var paths = new List<string>();
    foreach (var a in rest)
    {
        if (a == "--allow-none") allowNone = true;
        else if (a.StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("unknown option " + a);
            return 2;
        }
        else paths.Add(a);
    }
    if (paths.Count < 2)
    {
        Console.Error.WriteLine("usage: --crefs <xml> <dll> [<dll> ...] [--allow-none]");
        return 2;
    }

    var streams = new List<Stream>();
    var readers = new List<PEReader>();
    try
    {
        // Doc-comment ids write a nested type as Outer.Inner, so these keys use dots too. The first assembly that has a type decides.
        var types = new Dictionary<string, (MetadataReader Md, TypeDefinition Def)>(StringComparer.Ordinal);
        foreach (var dll in paths.Skip(1))
        {
            var s = File.OpenRead(dll);
            streams.Add(s);
            var pe = new PEReader(s);
            readers.Add(pe);
            var md = pe.GetMetadataReader();
            foreach (var h in md.TypeDefinitions)
            {
                var def = md.GetTypeDefinition(h);
                types.TryAdd(FullName(md, def, '.'), (md, def));
            }
        }

        var bad = new List<string>();
        var checkedCount = 0;
        foreach (var id in XDocument.Load(paths[0]).Descendants().Attributes("cref").Select(a => a.Value).Distinct())
        {
            if (id.StartsWith("!:", StringComparison.Ordinal)) { bad.Add(id + " (unresolved)"); continue; }
            if (id.Length < 3 || id[1] != ':' || id[0] == 'N') continue;
            var name = id[2..];
            var paren = name.IndexOf('(');
            // A method id names one overload: with a parameter list it has that many parameters, without one it has none.
            int? paramCount = id[0] != 'M' ? null : paren >= 0 ? CountParameters(name[paren..]) : 0;
            if (paren >= 0) name = name[..paren];
            string? member = null;
            if (id[0] != 'T')
            {
                var dot = name.LastIndexOf('.');
                if (dot < 0) continue;
                member = name[(dot + 1)..];
                name = name[..dot];
            }
            if (!types.TryGetValue(name, out var found))
            {
                // Types of System and other libraries are not ours to check; a missing Ageledger type is a failure.
                if (name.StartsWith("Ageledger.", StringComparison.Ordinal)) bad.Add(id + " (type not found)");
                continue;
            }
            checkedCount++;
            var (md2, type) = found;
            if (!IsVisible(md2, type)) { bad.Add(id); continue; }
            if (member is null) continue;
            if (!MemberExposed(md2, type, member, paramCount)) bad.Add(id);
        }

        Console.WriteLine($"{checkedCount} cref(s) into {string.Join(", ", paths.Skip(1).Select(Path.GetFileName))} checked in {Path.GetFileName(paths[0])}.");
        foreach (var b in bad) Console.WriteLine("::error::A doc comment links to a member that is not public or protected, or not found: " + b);
        if (checkedCount == 0 && !allowNone)
        {
            Console.Error.WriteLine("no cref into the assemblies was found, so this check would prove nothing.");
            return 2;
        }
        return bad.Count == 0 ? 0 : 1;
    }
    catch (Exception e) when (e is IOException or BadImageFormatException or UnauthorizedAccessException or XmlException)
    {
        Console.Error.WriteLine("cannot read the input: " + e.GetType().Name + ": " + e.Message);
        return 2;
    }
    finally
    {
        foreach (var r in readers) r.Dispose();
        foreach (var s in streams) s.Dispose();
    }
}

// "(A,B{C,D},E[])" has three parameters: commas inside {} (generic arguments) or [] do not count.
static int CountParameters(string list)
{
    var end = list.LastIndexOf(')');
    var inner = end > 0 ? list[1..end] : list[1..];
    if (inner.Length == 0) return 0;
    int depth = 0, count = 1;
    foreach (var c in inner)
    {
        if (c is '{' or '[') depth++;
        else if (c is '}' or ']') depth--;
        else if (c == ',' && depth == 0) count++;
    }
    return count;
}

// The number of parameters in a method's signature (the blob: calling convention, [generic parameter count], parameter count, ...).
static int SignatureParameterCount(MetadataReader md, MethodDefinition d)
{
    var r = md.GetBlobReader(d.Signature);
    var header = r.ReadSignatureHeader();
    if (header.IsGeneric) r.ReadCompressedInteger();
    return r.ReadCompressedInteger();
}

static bool MemberExposed(MetadataReader md, TypeDefinition type, string member, int? paramCount)
{
    var tick = member.IndexOf("``", StringComparison.Ordinal);
    if (tick >= 0) member = member[..tick];
    var methodName = member == "#ctor" ? ".ctor" : member;
    foreach (var m in type.GetMethods())
    {
        var d = md.GetMethodDefinition(m);
        // With a parameter list in the cref, only the overload with that many parameters counts; an internal overload does not make a public one's link valid.
        if (md.GetString(d.Name) == methodName && (paramCount is null || SignatureParameterCount(md, d) == paramCount)
            && Exposed(d.Attributes & MethodAttributes.MemberAccessMask)) return true;
    }
    foreach (var f in type.GetFields())
    {
        var d = md.GetFieldDefinition(f);
        if (md.GetString(d.Name) == member && Exposed((MethodAttributes)((int)d.Attributes & (int)FieldAttributes.FieldAccessMask))) return true;
    }
    foreach (var p in type.GetProperties())
    {
        var d = md.GetPropertyDefinition(p);
        if (md.GetString(d.Name) != member) continue;
        var acc = d.GetAccessors();
        foreach (var a in new[] { acc.Getter, acc.Setter })
            if (!a.IsNil && Exposed(md.GetMethodDefinition(a).Attributes & MethodAttributes.MemberAccessMask)) return true;
    }
    foreach (var e in type.GetEvents())
    {
        var d = md.GetEventDefinition(e);
        if (md.GetString(d.Name) != member) continue;
        var add = d.GetAccessors().Adder;
        if (!add.IsNil && Exposed(md.GetMethodDefinition(add).Attributes & MethodAttributes.MemberAccessMask)) return true;
    }
    return false;
}

// Public (6), family (4) and family-or-assembly (5) are visible to an app; field and method access masks use the same values.
static bool Exposed(MethodAttributes access) => access is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

static bool IsVisible(MetadataReader md, TypeDefinition def)
{
    while (true)
    {
        var v = def.Attributes & TypeAttributes.VisibilityMask;
        if (v is not (TypeAttributes.Public or TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem)) return false;
        var outer = def.GetDeclaringType();
        if (outer.IsNil) return true;
        def = md.GetTypeDefinition(outer);
    }
}
