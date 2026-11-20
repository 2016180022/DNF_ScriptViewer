// DNF_ScriptViewer 데이터 검사 도구
// 사용: dotnet run --project tools/DNF_ScriptViewer.Check -- [Data 폴더] [--pending] [--info] [--run 구간] [--input 파일] [--vars]
//   Data 폴더를 생략하면 현재 폴더에서 위로 올라가며 Data/를 찾음
//   --pending : 측정 대기(빈 값) 항목을 하나씩 출력 (기본은 종류별 개수만)
//   --info    : 참고 항목(미구현 브랜치 등)을 하나씩 출력
//   --run     : 구간 하나를 런타임으로 실행해 타임라인 출력 (구간 Order 번호 또는 이름 일부, all = 전체)
//   --input   : --run에서 구간의 입력 시퀀스 대신 쓸 파일 (확인용 입력: tools/DNF_ScriptViewer.Check/TestInputs/)
//   --vars    : --run에서 Height 변화도 모두 출력 (기본은 Height 제외)
//   --tests   : TestInputs/tests.txt의 목록을 모두 실행 (레포 루트의 RunChecks.cmd가 이 옵션으로 실행해 결과 파일을 엶)
// 데이터 오류는 경고만 하고 종료 코드는 항상 0
using System.Text;
using System.Text.RegularExpressions;
using DNF_ScriptViewer.Core.Diagnostics;
using DNF_ScriptViewer.Core.Loading;
using DNF_ScriptViewer.Core.Model;
using DNF_ScriptViewer.Core.Runtime;
using DNF_ScriptViewer.Core.Validation;

Console.OutputEncoding = Encoding.UTF8;

string? Opt(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
var valueOpts = new[] { "--run", "--input" };
var showPending = args.Contains("--pending");
var showInfo = args.Contains("--info");
var showAllVars = args.Contains("--vars");
var runTarget = Opt("--run");
var inputOverride = Opt("--input");
var dataDir = args.Where((a, i) => !a.StartsWith("--") && (i == 0 || !valueOpts.Contains(args[i - 1]))).FirstOrDefault() ?? FindDataDir();
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

if (runTarget is not null) await RunSegments(set, runTarget, inputOverride, showAllVars);
if (args.Contains("--tests")) await RunTestList(set, dataDir, showAllVars);
return 0;

// ============================ 테스트 목록 일괄 실행 ============================

// TestInputs/tests.txt: 한 줄에 "<구간 Order> <입력 파일>" (# 주석). "all"은 모든 구간을 각자의 입력 시퀀스로 실행
static async Task RunTestList(DataSet set, string dataDir, bool showAllVars)
{
    var testDir = Path.GetFullPath(Path.Combine(dataDir, "..", "tools", "DNF_ScriptViewer.Check", "TestInputs"));
    var listFile = Path.Combine(testDir, "tests.txt");
    if (!File.Exists(listFile)) { Console.WriteLine($"\n테스트 목록 없음: TestInputs/tests.txt"); return; }

    var lines = (await File.ReadAllLinesAsync(listFile))
        .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
    Console.WriteLine($"\n================ 테스트 목록 {lines.Count}개 (TestInputs/tests.txt) ================");
    foreach (var line in lines)
    {
        var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts[0] == "all") { await RunSegments(set, "all", null, showAllVars); continue; }
        if (parts.Length < 2) { Console.WriteLine($"\n테스트 목록 형식 오류: \"{line}\" (구간 Order와 입력 파일)"); continue; }
        await RunSegments(set, parts[0], Path.Combine(testDir, parts[1]), showAllVars);
    }
}

// ============================ 런타임 실행 출력 ============================

static async Task RunSegments(DataSet set, string target, string? inputOverride, bool showAllVars)
{
    var segs = set.Segments?.Segments ?? new List<SegmentDef>();
    var chosen = target == "all" ? segs
        : int.TryParse(target, out var order) ? segs.Where(s => s.Order == order).ToList()
        : segs.Where(s => s.Name.Contains(target)).ToList();
    if (chosen.Count == 0)
    {
        Console.WriteLine($"\n구간 '{target}'을(를) 찾지 못함 (Order 번호 또는 이름 일부)");
        return;
    }

    InputSequence? overrideSeq = null;
    if (inputOverride is not null)
    {
        if (!File.Exists(inputOverride)) { Console.WriteLine($"\n입력 파일 없음: {inputOverride}"); return; }
        var diag = new DiagnosticBag();
        overrideSeq = InputSequenceLoader.Load(Path.GetFileName(inputOverride), await File.ReadAllTextAsync(inputOverride), diag);
        foreach (var d in diag.Items) Console.WriteLine($"  입력 파일 {d.Severity}: {d.Src} {d.Message}");
    }

    foreach (var seg in chosen)
    {
        var input = overrideSeq ?? set.Inputs.GetValueOrDefault(seg.Input);
        var r = SegmentRunner.Run(set, seg, input);
        PrintRun(seg, r, input, showAllVars);
    }
}

static void PrintRun(SegmentDef seg, SegmentResult r, InputSequence? input, bool showAllVars)
{
    Console.WriteLine();
    Console.WriteLine($"######## 실행: 구간 '{seg.Name}' — 길이 {r.DurationMs}ms, 입력 {(input is null ? "없음" : $"{input.File} {input.Inputs.Count}개")} ########");

    Console.WriteLine($"\n== 상태 {r.States.Count}개 ==");
    foreach (var s in r.States)
        Console.WriteLine($"  {s.Start,6} ~ {s.End,6}ms  {s.State}{(s.LoopCount > 0 ? $" (Loop {s.LoopCount})" : "")}");

    Console.WriteLine($"\n== 전이 {r.Transitions.Count}개 ==");
    foreach (var t in r.Transitions)
        Console.WriteLine($"  {t.Time,6}ms  {t.From} → {t.To}  [{t.Condition}]  {t.Src}");

    if (r.Skips.Count > 0)
    {
        Console.WriteLine($"\n== 조건은 참이지만 건너뛴 브랜치 {r.Skips.Count}개 ==");
        foreach (var s in r.Skips) Console.WriteLine($"  {s.Time,6}ms  {s.State} → {s.Target}: {s.Reason}  {s.Src}");
    }

    if (r.CoolTimes.Count > 0)
    {
        Console.WriteLine($"\n== 쿨타임 {r.CoolTimes.Count}개 ==");
        foreach (var c in r.CoolTimes) Console.WriteLine($"  스킬 {c.SkillKey}  {c.Start,6} ~ {c.End,6}ms  ({c.Action})");
    }

    var varChanges = r.Vars.Where(v => showAllVars || v.Name != "Height").ToList();
    Console.WriteLine($"\n== 변수 변화 {varChanges.Count}개{(showAllVars ? "" : " (Height 제외, --vars로 전체)")} ==");
    foreach (var v in varChanges) Console.WriteLine($"  {v.Time,6}ms  {v.Name}: {v.Old} → {v.New}  {v.Src}");
    var heights = r.Vars.Where(v => v.Name == "Height").ToList();
    if (heights.Count > 0 && !showAllVars)
        Console.WriteLine($"  (Height 변화 {heights.Count}개, 최고 {heights.Max(h => h.New.AsNumber):0.#})");

    Console.WriteLine($"\n== 타격 {r.Hits.Count}개, 합계 {r.TotalDamagePercent:N0}% ==");
    foreach (var g in r.Hits.GroupBy(h => (h.BuffKey, h.BuffName)))
        Console.WriteLine($"  {g.Key.BuffKey} {g.Key.BuffName}: {g.Count()}회, 1회 {g.First().BasePercent:N1}% × 배율 {g.First().Multiplier:0.###} = {g.First().DamagePercent:N0}%  (첫 타격 {g.First().Time}ms)");

    if (r.Buffs.Count > 0)
    {
        Console.WriteLine($"\n== 버프·상태 이상 {r.Buffs.Count}개 ==");
        foreach (var b in r.Buffs)
            Console.WriteLine($"  {(b.Target == BuffTarget.Player ? "플레이어" : "허수아비")}  {b.BuffKey} {b.Name}  {b.Start} ~ {(b.End?.ToString() ?? "끝까지")}ms");
    }

    Console.WriteLine($"\n== 입력 {r.Inputs.Count}개 ==");
    foreach (var i in r.Inputs)
    {
        var res = i.Result switch { InputResult.Used => $"사용 {i.ResultTime}ms ({i.UsedBy})", InputResult.Discarded => $"버림 {i.ResultTime}ms (상태 변경)", _ => "안 씀" };
        Console.WriteLine($"  {i.PressTime,6}ms  {i.Command}: {res}");
    }

    if (r.Marks.Count > 0)
    {
        Console.WriteLine($"\n== Mark 대조 (시뮬레이션 - 원작) ==");
        foreach (var m in r.Marks)
            Console.WriteLine($"  {m.Mark.Time,6}ms  {m.Mark.State,-22} 시뮬레이션 {(m.SimTime?.ToString() ?? "없음"),6}  차이 {(m.DiffMs is int d ? $"{d:+0;-0;0}ms" : "-")}");
    }

    var warns = r.Warnings.Items;
    Console.WriteLine($"\n== 실행 경고 {warns.Count}개 ==");
    foreach (var g in warns.GroupBy(w => w.Code).OrderBy(g => g.Key))
    {
        Console.WriteLine($"[{g.Key}] {DiagnosticCodes.Describe(g.Key)} ({g.Count()})");
        foreach (var w in g) Console.WriteLine($"  {w.Src,-26} {w.Message}");
    }
}

// ============================ 검사 결과 출력 ============================

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
