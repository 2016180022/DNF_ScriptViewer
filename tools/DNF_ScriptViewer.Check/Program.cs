// DNF_ScriptViewer 데이터 검사 도구
// 사용: dotnet run --project tools/DNF_ScriptViewer.Check -- [Data 폴더] [--pending] [--info]
//   Data 폴더를 생략하면 현재 폴더에서 위로 올라가며 Data/를 찾음
//   --pending : 측정 대기(빈 값) 항목을 하나씩 출력 (기본은 종류별 개수만)
//   --info    : 참고 항목(미구현 브랜치 등)을 하나씩 출력
// 데이터 오류는 경고만 하고 종료 코드는 항상 0
using System.Text;
using System.Text.RegularExpressions;
using DNF_ScriptViewer.Core.Diagnostics;
using DNF_ScriptViewer.Core.Loading;
using DNF_ScriptViewer.Core.Validation;

Console.OutputEncoding = Encoding.UTF8;

var showPending = args.Contains("--pending");
var showInfo = args.Contains("--info");
var dataDir = args.FirstOrDefault(a => !a.StartsWith("--")) ?? FindDataDir();
if (dataDir is null || !Directory.Exists(dataDir))
{
    Console.WriteLine("Data 폴더를 찾지 못함. 경로를 인자로 넘겨 주세요.");
    return 0;
}
dataDir = Path.GetFullPath(dataDir);
Console.WriteLine($"검사 대상: {dataDir}");

var set = await DataLoader.LoadAsync(new FileDataSource(dataDir));
DataValidator.Validate(set);
var all = set.Diagnostics.Items;

Console.WriteLine($"액션 {set.ActionScript?.ActionList.Count ?? 0} · Func {set.ActionScript?.FuncList.Count ?? 0} · " +
                  $"스킬 {set.SkillInfo?.SkillList.Count ?? 0} · 버프 {set.BuffInfo?.BuffList.Count ?? 0} · " +
                  $"구간 {set.Segments?.Segments.Count ?? 0} · 입력 시퀀스 {set.Inputs.Count}/{set.Inputs.Count + set.MissingInputs.Count}");
Console.WriteLine();

PrintList("오류", all.Where(d => d.Severity == Severity.Error));
PrintList("경고", all.Where(d => d.Severity == Severity.Warning));

var pending = all.Where(d => d.Severity == Severity.Pending).ToList();
if (showPending) PrintList("측정 대기", pending);
else PrintSummary("측정 대기", pending, "--pending");

var info = all.Where(d => d.Severity == Severity.Info).ToList();
if (showInfo) PrintList("참고", info);
else PrintSummary("참고", info, "--info");

Console.WriteLine($"합계: 오류 {all.Count(d => d.Severity == Severity.Error)} · 경고 {all.Count(d => d.Severity == Severity.Warning)} · " +
                  $"측정 대기 {pending.Count} · 참고 {info.Count}");
return 0;

static void PrintList(string title, IEnumerable<Diagnostic> items)
{
    var list = items.ToList();
    Console.WriteLine($"== {title} {list.Count}건 ==");
    foreach (var group in list.GroupBy(d => d.Code).OrderBy(g => g.Key))
    {
        Console.WriteLine($"[{group.Key}] {DiagnosticCodes.Describe(group.Key)} ({group.Count()})");
        foreach (var d in group.OrderBy(d => d.Src.File).ThenBy(d => d.Src.Line))
            Console.WriteLine($"  {d.Src,-26} {d.Message}");
    }
    Console.WriteLine();
}

// 종류별 개수: 같은 코드 안에서 "…: 속성 비어 있음"의 속성 이름별로 묶음
static void PrintSummary(string title, List<Diagnostic> items, string flag)
{
    Console.WriteLine($"== {title} {items.Count}건 (하나씩 보려면 {flag}) ==");
    foreach (var group in items.GroupBy(d => d.Code).OrderBy(g => g.Key))
    {
        var byAttr = group.GroupBy(d => Regex.Match(d.Message, @": (\w+) 비어 있음$") is { Success: true } m ? m.Groups[1].Value : "")
                          .OrderByDescending(g => g.Count())
                          .Select(g => g.Key.Length > 0 ? $"{g.Key} {g.Count()}" : $"{g.Count()}");
        Console.WriteLine($"[{group.Key}] {DiagnosticCodes.Describe(group.Key)}: {string.Join(", ", byAttr)}");
    }
    Console.WriteLine();
}

static string? FindDataDir()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "Data");
        if (File.Exists(Path.Combine(candidate, DataLoader.ActionScriptFile))) return candidate;
    }
    return null;
}

sealed class FileDataSource(string root) : IDataSource
{
    public async Task<string?> ReadTextAsync(string relativePath)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
    }
}
