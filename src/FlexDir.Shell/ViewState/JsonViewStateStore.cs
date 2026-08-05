using System.Text.Json;
using System.Text.Json.Serialization;

using FlexDir.Core.Locations;
using FlexDir.Core.Sorting;
using FlexDir.Core.ViewState;

namespace FlexDir.Shell.ViewState;

/// <summary>
/// 뷰 상태를 파일 하나에 담는 <see cref="IViewStateStore"/> 구현체.
/// 기본 위치는 <c>%LOCALAPPDATA%\flex-dir\</c> 다 (docs/ARCHITECTURE.md §4).
/// <para>
/// <b>뷰 상태는 캐시다</b> (CLAUDE.md §4). 읽지 못하면 기본값으로 가고 예외를 밖으로 내지
/// 않는다 — 폴더를 못 여는 것과 뷰 설정을 못 읽는 것은 사용자에게 전혀 다른 사건이며,
/// 후자로 목록을 막으면 파일이 사라진 것처럼 보인다.
/// </para>
/// </summary>
public sealed class JsonViewStateStore : IViewStateStore
{
    public const string FileName = "view-state.json";

    // 사람이 열어 고칠 수 있어야 한다. enum 을 숫자로 저장하면 멤버 순서를 바꾸는 순간
    // 저장된 뷰가 조용히 다른 뷰로 바뀐다.
    private static readonly JsonSerializerOptions Format = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // 저장은 읽고-고치고-쓰는 순서라 겹치면 한쪽 변경이 사라진다.
    private readonly SemaphoreSlim writeGate = new(1, 1);

    private readonly string path;
    private readonly string temporaryPath;

    /// <param name="directory">상태 파일을 둘 폴더. 없으면 저장할 때 만든다.</param>
    public JsonViewStateStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        DirectoryPath = directory;
        path = Path.Combine(directory, FileName);
        temporaryPath = path + ".tmp";
    }

    /// <summary>기본 저장 위치. <c>%LOCALAPPDATA%\flex-dir\</c>.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "flex-dir");

    /// <summary>상태 파일이 놓인 폴더.</summary>
    public string DirectoryPath { get; }

    public async ValueTask<FolderViewState?> TryLoadAsync(LocationId folder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ct.ThrowIfCancellationRequested();

        var document = await ReadAsync(ct).ConfigureAwait(false);

        if (document.Folders is null || !document.Folders.TryGetValue(Key(folder), out var stored))
        {
            return null;
        }

        // 한 폴더의 값이 깨져도 나머지 기억은 살린다. 파일 전체를 버리면 폴더 하나를
        // 손으로 잘못 고친 대가로 수백 개 기억이 함께 사라진다.
        return ToFolderState(stored);
    }

    public async ValueTask SaveAsync(LocationId folder, FolderViewState state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(state);
        ct.ThrowIfCancellationRequested();

        await MutateAsync(
            document =>
            {
                var folders = document.Folders is null
                    ? []
                    : new Dictionary<string, FolderRecord>(document.Folders, StringComparer.Ordinal);

                folders[Key(folder)] = new FolderRecord(state.Mode, [.. state.Sort]);

                return document with { Folders = folders };
            },
            ct).ConfigureAwait(false);
    }

    public async ValueTask<GlobalViewState> LoadGlobalAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var document = await ReadAsync(ct).ConfigureAwait(false);

        // 전역 상태는 null 을 내지 않는다 — 호출자가 기본값을 조립하면 기본값이 두 군데에 생긴다.
        return ToGlobalState(document.Global) ?? GlobalViewState.Default;
    }

    public async ValueTask SaveGlobalAsync(GlobalViewState state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);
        ct.ThrowIfCancellationRequested();

        await MutateAsync(
            document => document with
            {
                // 경로는 표시형으로 저장한다 — \\?\ 접두사 없는 쪽이 파일을 읽는 사람에게 낫고
                // TryParse 가 도로 붙여 준다.
                Global = new GlobalRecord(
                    state.SplitterRatio,
                    state.Window,
                    state.LeftFolder?.DisplayPath,
                    state.RightFolder?.DisplayPath),
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 폴더 키. Windows 파일시스템은 대소문자를 구분하지 않으므로 주소창에 소문자로
    /// 입력했다고 뷰 설정이 따로 생기면 "기억한다" 가 성립하지 않는다.
    /// </summary>
    private static string Key(LocationId folder) => folder.Value.ToUpperInvariant();

    private static FolderViewState? ToFolderState(FolderRecord stored)
    {
        try
        {
            return new FolderViewState(stored.Mode, stored.Sort ?? []);
        }
        catch (ArgumentException)
        {
            // 정렬이 비었거나 값이 깨졌다. 기억이 없는 것으로 본다.
            return null;
        }
    }

    private static GlobalViewState? ToGlobalState(GlobalRecord? stored)
    {
        if (stored is null)
        {
            return null;
        }

        try
        {
            return new GlobalViewState(
                stored.SplitterRatio,
                stored.Window,
                ParseFolder(stored.LeftFolder),
                ParseFolder(stored.RightFolder));
        }
        catch (ArgumentOutOfRangeException)
        {
            // 스플리터 비율이 범위를 벗어났다. 그대로 쓰면 페인이 사라진다.
            return null;
        }
    }

    private async ValueTask<Document> ReadAsync(CancellationToken ct)
    {
        try
        {
            var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

            return JsonSerializer.Deserialize<Document>(text, Format) ?? new Document();
        }
        catch (FileNotFoundException)
        {
            return new Document();   // 첫 실행. 오류가 아니다.
        }
        catch (DirectoryNotFoundException)
        {
            return new Document();
        }
        catch (JsonException)
        {
            return new Document();   // 깨진 파일 — 기본값으로 간다. 저장하면 되살아난다.
        }
        catch (IOException)
        {
            return new Document();   // 잠김·읽기 실패. 캐시이므로 물러난다.
        }
        catch (UnauthorizedAccessException)
        {
            return new Document();
        }
    }

    private async ValueTask MutateAsync(Func<Document, Document> change, CancellationToken ct)
    {
        await writeGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var next = change(await ReadAsync(ct).ConfigureAwait(false));

            Directory.CreateDirectory(DirectoryPath);

            // 제자리에서 덮어쓰면 쓰다 죽었을 때 반쪽 파일이 남고, 그 파일은 다음 실행에서
            // 손상으로 읽혀 폴더별 기억이 통째로 날아간다. 임시 파일에 쓰고 옮긴다.
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(next, Format), ct)
                .ConfigureAwait(false);

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            writeGate.Release();
        }
    }

    // 저장 형식은 Core 의 타입과 분리한다. 그쪽 생성자는 값을 검증하므로 역직렬화가
    // 곧바로 예외를 던지는데, 그러면 깨진 파일 하나가 폴더를 여는 길을 막는다.
    private sealed record Document
    {
        public GlobalRecord? Global { get; init; }

        public Dictionary<string, FolderRecord>? Folders { get; init; }
    }

    /// <summary>깨진 경로는 기억이 없는 것으로 다룬다 — 복원이 폴백으로 가면 된다.</summary>
    private static LocationId? ParseFolder(string? path)
        => path is not null && LocationId.TryParse(path, out var folder, out _) ? folder : null;

    private sealed record GlobalRecord(
        double SplitterRatio,
        WindowPlacement? Window,
        string? LeftFolder = null,
        string? RightFolder = null);

    private sealed record FolderRecord(ViewMode Mode, List<SortOrder>? Sort);
}
