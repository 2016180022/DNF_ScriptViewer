namespace DNF_ScriptViewer.Core.Validation;

/// <summary>진단 코드 설명 (콘솔 검사 도구·뷰어 경고 목록의 묶음 제목)</summary>
public static class DiagnosticCodes
{
    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        ["FILE001"] = "데이터 파일 없음",
        ["XML001"] = "XML 형식 오류",

        ["AS001"] = "이름 중복 (액션·Func·변수·상수)",
        ["AS002"] = "알 수 없는 요소",
        ["AS003"] = "브랜치 대상 액션 없음",
        ["AS004"] = "ActionIgnore 표시가 남아 있음 (대상 액션이 생김)",
        ["AS005"] = "정의되지 않은 Func 참조",
        ["AS006"] = "Func 정의·참조 위치 오류",
        ["AS007"] = "ActionEnd() 안전장치 브랜치 없음",
        ["AS008"] = "SuperCancelBranch에 ActionEnd() 조건",
        ["AS009"] = "선언되지 않은 변수",
        ["AS010"] = "상수 값 변경",
        ["AS011"] = "상수 값 측정 대기",
        ["AS012"] = "남은 임시 토큰",
        ["AS013"] = "열거 값 오류",
        ["AS014"] = "빈 값 (측정 대기)",
        ["AS015"] = "시간 값 형식 오류",
        ["AS016"] = "액션 SkillKey 형식 오류",
        ["AS017"] = "참조하지 않는 Func",
        ["AS018"] = "미구현 액션으로 가는 브랜치",
        ["AS019"] = "변수·상수 이름 형식",
        ["AS020"] = "스킬이 아닌 액션의 CoolTime",

        ["LNK001"] = "SkillInfo에 없는 스킬 키",
        ["LNK002"] = "BuffIndex 형식 오류",
        ["LNK003"] = "BuffIndex가 BuffKey 목록 길이를 넘음",
        ["LNK004"] = "BuffInfo에 없는 BuffKey",
        ["LNK005"] = "CheckBuff 키 오류",
        ["LNK006"] = "BuffEdit 대상 오류",
        ["LNK007"] = "식이 참조하는 스킬·값 없음",
        ["LNK008"] = "SkillLearned VP 없음",
        ["LNK009"] = "장전 대상 변수 없음",

        ["EXP001"] = "식 문법 오류",
        ["EXP002"] = "알 수 없는 이름·함수",
        ["EXP003"] = "함수 인자 오류",

        ["SI001"] = "SkillKey 중복",
        ["SI002"] = "SkillInfo 값 형식 오류",
        ["BI001"] = "BuffKey 중복",
        ["BI002"] = "BuffInfo 값 형식 오류",
        ["BI003"] = "BuffInfo 열거 값 오류",

        ["SEG000"] = "Segments 알 수 없는 요소",
        ["SEG001"] = "없는 영상 Id",
        ["SEG002"] = "VideoStart·VideoEnd",
        ["SEG003"] = "세팅 오류 (스킬·레벨·VP)",
        ["SEG004"] = "시작 State",
        ["SEG005"] = "시작 변수 이름",
        ["SEG006"] = "Mark State 없음",
        ["SEG007"] = "Mark 시각",
        ["SEG008"] = "입력 시퀀스 파일",
        ["SEG009"] = "시작 변수 값",
        ["SEG010"] = "Segments 숫자 형식 오류",
        ["SEG011"] = "영상 Source 자리 표시",
        ["SEG012"] = "구간 Order 중복",
        ["SEG013"] = "Data 폴더 밖을 가리키는 경로",
        ["SEG014"] = "시작 State로 들어오는 브랜치 조건과 시작값이 맞지 않음",

        ["INP000"] = "입력 시퀀스 알 수 없는 요소",
        ["INP001"] = "입력 Command 오류",
        ["INP002"] = "입력 시각·Hold",
        ["INP003"] = "입력 숫자 형식 오류",

        // 런타임 (구간 실행 결과의 경고)
        ["RUN001"] = "임시값 사용 (빈 측정값)",
        ["RUN002"] = "알 수 없는 이름·함수",
        ["RUN003"] = "데이터 값 계산 불가",
        ["RUN004"] = "입력 시퀀스 없음",
        ["RUN005"] = "빈 Value 건너뜀",
    };

    public static string Describe(string code) => Descriptions.TryGetValue(code, out var d) ? d : code;
}
