using DNF_ScriptViewer.Core.Diagnostics;
using DNF_ScriptViewer.Core.Model;

namespace DNF_ScriptViewer.Core.Loading;

/// <summary>Data/ 전체를 읽은 결과. 읽지 못한 파일은 null (진단에 Error)</summary>
public sealed class DataSet
{
    public ActionScriptData? ActionScript { get; init; }
    public SkillInfoData? SkillInfo { get; init; }
    public BuffInfoData? BuffInfo { get; init; }
    public SegmentsData? Segments { get; init; }
    /// <summary>Segments의 Input 경로 → 입력 시퀀스 (파일이 없으면 들어 있지 않음)</summary>
    public Dictionary<string, InputSequence> Inputs { get; } = new();
    /// <summary>Segments가 가리키지만 아직 없는 입력 시퀀스 경로</summary>
    public HashSet<string> MissingInputs { get; } = new();
    public DiagnosticBag Diagnostics { get; } = new();
}

public static class DataLoader
{
    public const string ActionScriptFile = "ActionScript.xml";
    public const string SkillInfoFile = "SkillInfo.xml";
    public const string BuffInfoFile = "BuffInfo.xml";
    public const string SegmentsFile = "Segments.xml";

    public static async Task<DataSet> LoadAsync(IDataSource source)
    {
        var diag = new DiagnosticBag();

        async Task<T?> Read<T>(string file, Func<string, string, DiagnosticBag, T?> load) where T : class
        {
            var text = await source.ReadTextAsync(file);
            if (text is null)
            {
                diag.Error("FILE001", new SourceRef(file, 0), "파일이 없음");
                return null;
            }
            return load(file, text, diag);
        }

        var set = new DataSet
        {
            ActionScript = await Read(ActionScriptFile, ActionScriptLoader.Load),
            SkillInfo = await Read(SkillInfoFile, SkillInfoLoader.Load),
            BuffInfo = await Read(BuffInfoFile, BuffInfoLoader.Load),
            Segments = await Read(SegmentsFile, SegmentsLoader.Load),
        };
        foreach (var d in diag.Items) set.Diagnostics.Add(d);

        // 입력 시퀀스: 구간이 가리키는 파일만 (테이크마다 하나, 같은 파일을 가리켜도 됨)
        if (set.Segments is not null)
        {
            foreach (var path in set.Segments.Segments.Select(s => s.Input).Where(p => p.Length > 0).Distinct())
            {
                // 보안: Data/ 밖을 가리키는 경로(절대 경로, ..)는 읽지 않음
                if (!IsSafeRelativePath(path))
                {
                    set.Diagnostics.Warn("SEG013", new SourceRef(SegmentsFile, 0), $"Input 경로 \"{path}\"가 Data 폴더 밖을 가리켜 읽지 않음 (Data 기준 상대 경로만)");
                    continue;
                }
                var text = await source.ReadTextAsync(path);
                if (text is null) { set.MissingInputs.Add(path); continue; }
                var seq = InputSequenceLoader.Load(path, text, set.Diagnostics);
                if (seq is not null) set.Inputs[path] = seq;
            }
        }
        return set;
    }

    /// <summary>Data/ 기준 상대 경로인지: 드라이브·루트로 시작하지 않고, ..·:·\ 를 쓰지 않음 (구분자는 /)</summary>
    public static bool IsSafeRelativePath(string path) =>
        path.Length > 0
        && !path.StartsWith('/')
        && !path.Contains('\\')
        && !path.Contains(':')
        && path.Split('/').All(part => part.Length > 0 && part != "." && part != "..");
}
