using System.Text.RegularExpressions;

namespace DNF_ScriptViewer.Core.Expressions;

/// <summary>
/// 식에서 쓸 수 있는 이름의 원본 목록 (검증기·런타임이 함께 참조). 변수·상수는 ActionScript에서 읽음
/// </summary>
public static partial class KnownNames
{
    /// <summary>런타임 함수와 인자 수</summary>
    public static readonly IReadOnlyDictionary<string, int> Functions = new Dictionary<string, int>
    {
        ["ActionEnd"] = 0,            // 액션 EndTime 도달
        ["LoopCount"] = 0,            // 같은 액션으로 연속 재진입한 횟수 (1타 = 0)
        ["CheckBuff"] = 1,            // 지속 상태(BuffType State) 보유
        ["SkillLearned"] = 1,         // 스킬 키(습득) 또는 스킬 키 × 100 + VP
        ["getPlayerDirection"] = 0,   // 클라이언트 값: 입력 방향
        ["getCharacterMoveSpeed"] = 0,// 클라이언트 값: 이동 속도
        ["getSkillCoolTime"] = 1,     // SkillInfo 쿨타임 (ms)
        ["getSkillValue"] = 2,        // SkillInfo 사용 레벨의 {valueN}
        ["keyDown"] = 1,              // 클라 값: 그 커맨드를 누르고 있는 중 (입력의 Hold 구간)
    };

    /// <summary>입력 커맨드. Skill_{스킬 키}는 따로. 커맨드 이름은 아직 쓰지 않은 눌림(소비형), 누르고 있는 중은 keyDown(커맨드)</summary>
    public static readonly IReadOnlySet<string> InputCommands = new HashSet<string>
    {
        "Jump", "Attack", "Move", "Dash", "NitroDown",
    };

    public static readonly IReadOnlySet<string> Literals = new HashSet<string> { "True", "False", "None" };

    [GeneratedRegex(@"^Skill_(\d+)$")]
    private static partial Regex SkillCommandRegex();

    /// <summary>Skill_801 → 801</summary>
    public static int? SkillCommandKey(string name)
    {
        var m = SkillCommandRegex().Match(name);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    public static bool IsInputCommand(string name) => InputCommands.Contains(name) || SkillCommandKey(name) is not null;
}
