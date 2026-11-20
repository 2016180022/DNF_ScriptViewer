namespace DNF_ScriptViewer.Core.Loading;

/// <summary>
/// 데이터 폴더(Data/) 읽기. 콘솔 검사 도구는 파일 시스템, Web은 HTTP로 구현해서 로더 코드를 같이 씀
/// </summary>
public interface IDataSource
{
    /// <summary>Data/ 기준 상대 경로의 텍스트. 파일이 없으면 null</summary>
    Task<string?> ReadTextAsync(string relativePath);
}
