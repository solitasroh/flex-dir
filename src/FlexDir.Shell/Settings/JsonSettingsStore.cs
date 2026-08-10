using System.Text.Json;

using FlexDir.Core.Locations;
using FlexDir.Core.Settings;

namespace FlexDir.Shell.Settings;

/// <summary>
/// 설정을 파일 하나에 담는 <see cref="ISettingsStore"/> 구현체.
///
/// <para>
/// <b>뷰 상태와 다른 파일이다</b> (<c>JsonViewStateStore</c> 는 <c>view-state.json</c>).
/// 그쪽은 캐시라 깨지면 기본값으로 접고 마는데, 설정이 같은 파일에 있으면 그 사건이
/// 사용자가 정해 둔 것을 함께 뒤집는다. <c>JsonFavoriteStore</c> 와 같은 판단이고
/// 실패를 다루는 방식도 그것을 그대로 따른다.
/// </para>
///
/// <para>
/// <b>깨진 파일을 조용히 덮어쓰지 않는다.</b> 기본값으로 읽고 다음 저장이 그대로 덮어쓰면
/// 영영 사라진다 — 원본을 <c>.bak</c> 으로 옆에 남기고 물러난다.
/// </para>
///
/// <para>
/// <b>값 하나가 깨졌다고 나머지를 버리지 않는다.</b> 파일이 JSON 으로 읽히기만 하면 항목별로
/// 판정한다 — 경로 하나를 손으로 고치다 오타를 냈다고 숨김 설정까지 잃으면 안 된다.
/// </para>
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    public const string FileName = "settings.json";

    // 사람이 열어 고칠 수 있어야 한다 — 되찾는 마지막 수단이 그것이다.
    private static readonly JsonSerializerOptions Format = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly SemaphoreSlim writeGate = new(1, 1);

    private readonly string path;
    private readonly string temporaryPath;
    private readonly string quarantinePath;

    /// <param name="directory">설정 파일을 둘 폴더. 없으면 저장할 때 만든다.</param>
    public JsonSettingsStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        DirectoryPath = directory;
        path = Path.Combine(directory, FileName);
        temporaryPath = path + ".tmp";
        quarantinePath = path + ".bak";
    }

    public string DirectoryPath { get; }

    public async ValueTask<AppSettings> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Document document;

        try
        {
            var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

            document = JsonSerializer.Deserialize<Document>(text, Format) ?? new Document();
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return AppSettings.Default;   // 첫 실행. 오류가 아니다.
        }
        catch (JsonException)
        {
            // 되찾을 곳이 없으므로 원본을 남긴다 (위 §요약).
            Quarantine();

            return AppSettings.Default;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 잠김·권한. 파일은 그대로 두고 이번만 물러난다 — 지우지 않는다.
            return AppSettings.Default;
        }

        return Parse(document);
    }

    public async ValueTask SaveAsync(AppSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ct.ThrowIfCancellationRequested();

        var document = new Document
        {
            StartMode = settings.StartMode.ToString(),
            StartFolder = settings.StartFolder?.DisplayPath,
            ShowHiddenItems = settings.ShowHiddenItems,
        };

        await writeGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(DirectoryPath);

            // 제자리에서 덮어쓰면 쓰다 죽었을 때 반쪽 파일이 정본이 된다.
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(document, Format), ct)
                .ConfigureAwait(false);

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <summary>
    /// 항목별로 판정한다 (위 §요약). 모르는 모드·깨진 경로는 그 항목만 기본값으로 접는다.
    /// </summary>
    private static AppSettings Parse(Document document)
    {
        var mode = Enum.TryParse<StartFolderMode>(document.StartMode, ignoreCase: true, out var parsed)
            ? parsed
            : AppSettings.Default.StartMode;

        var folder = document.StartFolder is { } text && LocationId.TryParse(text, out var location, out _)
            ? location
            : null;

        return new AppSettings
        {
            StartMode = mode,
            StartFolder = folder,
            ShowHiddenItems = document.ShowHiddenItems ?? AppSettings.Default.ShowHiddenItems,
        };
    }

    private void Quarantine()
    {
        try
        {
            File.Move(path, quarantinePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 옮기지 못했으면 원본이 그대로 남아 있다는 뜻이다. 그것도 보존이다.
        }
    }

    // 저장 형식은 Core 의 타입과 분리한다 — LocationId 는 문자열이 아니고 StartFolderMode 는
    // 숫자가 아니다. 역직렬화가 곧바로 검증에 걸리면 깨진 값 하나가 설정 전체를 막는다.
    // 셋 다 nullable 인 것은 "없음" 과 "기본값" 을 여기서 가르기 위해서다.
    private sealed record Document
    {
        public string? StartMode { get; init; }

        /// <summary>경로는 표시형으로 저장한다 — 사람이 열어 고칠 수 있어야 한다.</summary>
        public string? StartFolder { get; init; }

        public bool? ShowHiddenItems { get; init; }
    }
}
