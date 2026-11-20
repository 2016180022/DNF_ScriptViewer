using System.Globalization;
using DNF_ScriptViewer.Core.Diagnostics;

namespace DNF_ScriptViewer.Core.Model;

// ===================== SkillInfo =====================

/// <summary>레벨 L의 값 = Base + Grows × (L - 1). Row가 있으면 그 레벨은 Row 값</summary>
public sealed record LevelInfoDef(string Variant,
    IReadOnlyDictionary<string, string> Base,
    IReadOnlyDictionary<string, string> Grows,
    IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> Rows,
    SourceRef Src)
{
    /// <summary>이 이름의 값이 있는지 (Base 또는 Row)</summary>
    public bool HasValue(string name) => Base.ContainsKey(name) || Rows.Values.Any(r => r.ContainsKey(name));

    /// <summary>레벨 값 계산. 숫자가 아니면 null ("Null", "-" 등)</summary>
    public double? GetValue(string name, int level)
    {
        if (Rows.TryGetValue(level, out var row) && row.TryGetValue(name, out var rv)) return Num(rv);
        if (!Base.TryGetValue(name, out var bv)) return null;
        var b = Num(bv);
        if (b is null) return null;
        var g = Grows.TryGetValue(name, out var gv) ? Num(gv) ?? 0 : 0;
        var v = b.Value + g * (level - 1);
        // 1레벨 값이 정수인 항목은 반올림 (SkillInfo 머리말)
        return b.Value == Math.Floor(b.Value) ? Math.Round(v, MidpointRounding.AwayFromZero) : v;
    }

    private static double? Num(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}

public sealed record EvolutionDef(int Type, string Name, SourceRef Src);

public sealed record SkillDef(int Key, string Name, string Type, int MaxLevel,
    IReadOnlyList<int> BuffKeys, IReadOnlyList<LevelInfoDef> LevelInfos, IReadOnlyList<EvolutionDef> Evolutions,
    SourceRef Src)
{
    public LevelInfoDef? BaseLevelInfo => LevelInfos.FirstOrDefault(l => l.Variant == "Base") ?? LevelInfos.FirstOrDefault();
}

public sealed class SkillInfoData
{
    public string File { get; init; } = "";
    public List<SkillDef> SkillList { get; } = new();
    public Dictionary<int, SkillDef> Skills { get; } = new();
}

// ===================== BuffInfo =====================

public sealed record BuffDef(int Key, string Name, BuffType BuffType, string BuffTypeRaw, string Group, int? SkillKey,
    string Remain, StackType StackType, string StackTypeRaw, string Desc,
    // Params: Param01부터 순서대로 (빠진 번호는 null)
    IReadOnlyList<string?> Params, SourceRef Src);

public sealed class BuffInfoData
{
    public string File { get; init; } = "";
    public List<BuffDef> BuffList { get; } = new();
    public Dictionary<int, BuffDef> Buffs { get; } = new();
}

// ===================== Segments =====================

public sealed record VideoDef(string Id, string Source, SourceRef Src);

/// <summary>스킬별 세팅. DefaultSetting(공통)과 구간 Setting(우선)이 같은 형식</summary>
public sealed record SkillSetting(int? Skill, int? Level, int? VP, SourceRef Src);

public sealed record StartVariable(string Name, string Value, SourceRef Src);

/// <summary>원작 측정값 — 구간 시작 기준 ms, 상태 변경만</summary>
public sealed record MarkDef(int? Time, string State, SourceRef Src);

public sealed record SegmentDef(string Name, int? Order, string Combo, string Video,
    int? VideoStart, int? VideoEnd, string Input,
    IReadOnlyList<SkillSetting> Settings, string StartState, IReadOnlyList<StartVariable> StartVariables,
    IReadOnlyList<MarkDef> Marks, SourceRef Src)
{
    public int? DurationMs => VideoStart is int s && VideoEnd is int e ? e - s : null;
}

public sealed class SegmentsData
{
    public string File { get; init; } = "";
    public List<VideoDef> Videos { get; } = new();
    public List<SkillSetting> DefaultSettings { get; } = new();
    public List<SegmentDef> Segments { get; } = new();
}

// ===================== 입력 시퀀스 =====================

/// <summary>Hold가 있으면 Time~Time+Hold 동안 KeyDown 참</summary>
public sealed record InputEvent(int? Time, string Command, int? Hold, SourceRef Src);

public sealed class InputSequence
{
    public string File { get; init; } = "";
    public string Name { get; init; } = "";
    public List<InputEvent> Inputs { get; } = new();
}
