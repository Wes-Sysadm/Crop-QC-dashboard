using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CropQc.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace CropQc.Api.Tests;

/// <summary>
/// Compiled call-path tripwire, complemented by BrowserAntiforgeryTests runtime evidence.
/// Follows application methods, async bodies, delegates and application interface implementations.
/// Not a proof about reflection-generated calls or the internals of external libraries.
/// </summary>
public sealed class GetMutationArchitectureTests
{
    // Temporary exceptions only. See "Compatibility/bootstrap request-time mutation cleanup"
    // in docs/batch-1c-antiforgery-browser-writes.md. Never exempt a controller or route prefix.
    private static readonly (string Caller, string Helper, string Purpose)[] Exceptions =
    [
        ("AdminManagementService.GetConfigurationAsync", "AdminManagementService.EnsureConfigurationTableAsync", "Configuration schema compatibility"),
        ("AdminManagementService.GetConfigurationAsync", "AdminManagementService.EnsureConfigurationDefaultsAsync", "Configuration missing defaults (same exception)"),
        ("QcStationAdminService.GetStationsAsync", "QcStationAdminService.EnsureQcStationColumnsAsync", "Station schema and legacy name backfill"),
        ("GoogleCredentialStore.GetDiagnosticAsync", "GoogleCredentialStore.EnsureSchemaAsync", "Credential diagnostic schema compatibility")
    ];

    private static readonly Assembly[] Assemblies =
    [typeof(AdminController).Assembly, typeof(CropQc.Data.CropQcDbContext).Assembly, typeof(CropQc.Shared.Security.QcStationApiKeyValidator).Assembly];
    private static readonly Type[] Types = Assemblies.SelectMany(a => a.GetTypes()).ToArray();
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(c => c.Value);

    [Fact]
    public void GetCallPaths_HaveOnlyTheThreeExplicitCompatibilityExceptions()
    {
        var roots = typeof(AdminController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.IsDefined(typeof(HttpGetAttribute))
                || m.DeclaringType == typeof(HomeController) && m.Name is "Index" or "Error")
            .ToArray();
        Assert.NotEmpty(roots);
        var seenExceptions = new HashSet<(string, string)>();
        var violations = roots.SelectMany(root => Scan(root, seenExceptions)).Distinct().Order().ToArray();
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
        Assert.Equal(Exceptions.Select(e => (e.Caller, e.Helper)).Order(), seenExceptions.Order());
        Assert.Equal(3, Exceptions.Select(e => e.Caller).Distinct().Count());
    }

    [Fact]
    public void GuardDetectsAnIndirectWrite_ButAllowsProcessLocalState()
    {
        Assert.Contains(Scan(typeof(Probe).GetMethod(nameof(Probe.Bad))!, []), x => x.Contains("SaveChanges", StringComparison.Ordinal));
        Assert.Empty(Scan(typeof(Probe).GetMethod(nameof(Probe.Read))!, []));
    }

    [Theory]
    [InlineData("AdminManagementService", "EnsureConfigurationTableAsync", "F0509784048387CC062E85271631679696FA027BD543844823A9AD1F09681819")]
    [InlineData("AdminManagementService", "EnsureConfigurationDefaultsAsync", "648ACFA2B08B9F5E60267E93D25D5E0C2E34814C573514EB81A4B2D4E4D9CAE6")]
    [InlineData("QcStationAdminService", "EnsureQcStationColumnsAsync", "C28DBD24A050D8D1BB4CEA9E83FFB840F2705B00A5DB480513E4BBD15587FCB2")]
    [InlineData("GoogleCredentialStore", "EnsureSchemaAsync", "1A706CEC3EB003C568A55FD8A34F9E72F6E193C86545AE761DD84C9D8BD1A657")]
    public void CompatibilityExceptionBodies_AreFrozenToReviewedBootstrapPurpose(string service, string helper, string expectedHash)
    {
        // Freeze these exceptional helper implementations, not whole services. Any change
        // requires the separately reviewed compatibility/startup follow-up, not a new allowlist.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CropQc.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "src", "CropQc.Web", "Services", service + ".cs"));
        var body = Regex.Match(source, @"(?ms)^    private async Task " + helper + @"\(.*?(?=^    (?:private|public)|\z)").Value
            .Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        Assert.NotEmpty(body);
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))));
    }

    [Fact]
    public void GuardDetectsAsyncAndInterfaceWrites_AndFileMutation()
    {
        foreach (var name in new[] { nameof(Probe.AsyncWrite), nameof(Probe.FileWrite) })
            Assert.NotEmpty(Scan(typeof(Probe).GetMethod(name)!, []));
        // Actual interface dispatch must reach the concrete application persistence method.
        Assert.NotEmpty(Scan(typeof(Probe).GetMethod(nameof(Probe.InterfaceWrite))!, []));
    }

    private static IEnumerable<string> Scan(MethodInfo root, HashSet<(string, string)> seenExceptions)
    {
        var visited = new HashSet<MethodBase>();
        var pending = new Stack<(MethodInfo Method, string Path)>();
        pending.Push((root, Name(root)));
        while (pending.TryPop(out var next))
        {
            if (!visited.Add(next.Method)) continue;
            var stateType = next.Method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
                ?? next.Method.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
            var body = stateType?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? next.Method;
            foreach (var called in Calls(body))
            {
                var name = Name(called);
                var exception = Exceptions.FirstOrDefault(e => e.Caller == Name(next.Method) && e.Helper == name);
                if (exception.Helper is not null)
                {
                    seenExceptions.Add((exception.Caller, exception.Helper));
                    continue;
                }
                if (IsWrite(called))
                {
                    yield return next.Path + " -> " + name;
                    continue;
                }
                if (called is not MethodInfo method) continue;
                if (method.DeclaringType?.IsInterface == true && Assemblies.Contains(method.DeclaringType.Assembly))
                {
                    foreach (var type in Types.Where(t => !t.IsAbstract && !t.IsInterface && !t.ContainsGenericParameters && method.DeclaringType.IsAssignableFrom(t)))
                    {
                        var map = type.GetInterfaceMap(method.DeclaringType);
                        var index = Array.FindIndex(map.InterfaceMethods, m => m == method);
                        if (index >= 0) pending.Push((map.TargetMethods[index], next.Path + " -> " + Name(map.TargetMethods[index])));
                    }
                }
                else if (Assemblies.Contains(method.Module.Assembly) || method.DeclaringType == typeof(Probe))
                {
                    pending.Push((method, next.Path + " -> " + name));
                }
            }
        }
    }

    private static bool IsWrite(MethodBase method)
    {
        var type = method.DeclaringType?.FullName ?? "";
        var name = method.Name;
        return type.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal)
                && (name.StartsWith("SaveChanges", StringComparison.Ordinal)
                    || name.StartsWith("ExecuteUpdate", StringComparison.Ordinal)
                    || name.StartsWith("ExecuteDelete", StringComparison.Ordinal)
                    || name.StartsWith("ExecuteSql", StringComparison.Ordinal)
                    || name.StartsWith("EnsureCreated", StringComparison.Ordinal)
                    || name.StartsWith("EnsureDeleted", StringComparison.Ordinal)
                    || name.StartsWith("Migrate", StringComparison.Ordinal))
            || type is "System.IO.File" or "System.IO.Directory"
                && (name.StartsWith("Write", StringComparison.Ordinal) || name.StartsWith("Append", StringComparison.Ordinal)
                    || name is "Delete" or "Move" or "Copy" or "Create" or "CreateDirectory" or "OpenWrite")
            || type == "System.Diagnostics.Process" && name == "Start";
    }

    private static string Name(MethodBase method) => method.DeclaringType!.Name + "." + method.Name;

    private static IEnumerable<MethodBase> Calls(MethodInfo method)
    {
        var bytes = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (var offset = 0; offset < bytes.Length;)
        {
            var first = bytes[offset++];
            var code = Codes[first == 0xfe ? (short)(0xfe00 | bytes[offset++]) : first];
            if (code.OperandType == OperandType.InlineMethod)
            {
                var called = method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset),
                    method.DeclaringType?.GetGenericArguments(), method.GetGenericArguments());
                if (called is not null) yield return called;
            }
            offset += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4
            };
        }
    }

    private static class Probe
    {
        public static Task<int> Bad(CropQc.Data.CropQcDbContext db) => Save(db);
        private static Task<int> Save(CropQc.Data.CropQcDbContext db) => db.SaveChangesAsync();
        public static async Task AsyncWrite(CropQc.Data.CropQcDbContext db) => await db.SaveChangesAsync();
        public static void FileWrite() => File.WriteAllText("not-executed.txt", "probe");
        public static Task InterfaceWrite(CropQc.Web.Services.IRunProjectionService service, System.Security.Claims.ClaimsPrincipal user) =>
            service.InspectDeletedAsync(1, user, default);
        public static int Read() => new Dictionary<string, int> { ["cache"] = 1 }.Count;
    }
}
