using System.Text.Json;

using FlexDir.Core.Favorites;
using FlexDir.Core.Locations;

namespace FlexDir.Shell.Favorites;

/// <summary>
/// 즐겨찾기를 파일 하나에 담는 <see cref="IFavoriteStore"/> 구현체.
///
/// <para>
/// <b>뷰 상태와 다른 파일이다</b> (<c>JsonViewStateStore</c> 는 <c>view-state.json</c>).
/// 그쪽은 캐시라 깨지면 기본값으로 접고 마는데, 즐겨찾기가 같은 파일에 있으면 그 사건이
/// 사용자가 모아 둔 목록을 함께 지운다.
/// </para>
///
/// <para>
/// <b>깨진 파일을 조용히 덮어쓰지 않는다.</b> 빈 목록으로 읽고 다음 저장이 그대로
/// 덮어쓰면 영영 사라진다 — 즐겨찾기는 파일시스템 어디에도 없어서 되찾을 곳이 없다
/// (<see cref="IFavoriteStore"/>). 원본을 <c>.bak</c> 으로 옆에 남기고 물러난다.
/// </para>
/// </summary>
public sealed class JsonFavoriteStore : IFavoriteStore
{
    public const string FileName = "favorites.json";

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

    /// <param name="directory">즐겨찾기 파일을 둘 폴더. 없으면 저장할 때 만든다.</param>
    public JsonFavoriteStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        DirectoryPath = directory;
        path = Path.Combine(directory, FileName);
        temporaryPath = path + ".tmp";
        quarantinePath = path + ".bak";
    }

    public string DirectoryPath { get; }

    public async ValueTask<IReadOnlyList<Favorite>> LoadAsync(CancellationToken ct)
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
            return [];   // 첫 실행. 오류가 아니다.
        }
        catch (JsonException)
        {
            // 되찾을 곳이 없으므로 원본을 남긴다 (위 §요약).
            Quarantine();

            return [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 잠김·권한. 파일은 그대로 두고 이번만 물러난다 — 지우지 않는다.
            return [];
        }

        return Parse(document);
    }

    public async ValueTask SaveAsync(IReadOnlyList<Favorite> favorites, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        ct.ThrowIfCancellationRequested();

        // 호출자가 나중에 자기 목록을 바꿔도 흔들리지 않게 지금 찍는다.
        var document = new Document
        {
            Favorites = [.. favorites.Select(favorite => new Record(favorite.Path.DisplayPath, favorite.Label))],
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
    /// 파싱되지 않는 항목은 건너뛴다 — 손으로 고치다 경로 하나가 깨졌다고 나머지를 잃지
    /// 않는다. 라벨이 없으면 폴더 이름을 쓴다 (빈 라벨은 트리에 빈 줄로 선다).
    /// </summary>
    private static IReadOnlyList<Favorite> Parse(Document document)
    {
        if (document.Favorites is not { Count: > 0 } records)
        {
            return [];
        }

        var favorites = new List<Favorite>(records.Count);

        foreach (var record in records)
        {
            if (LocationId.TryParse(record.Path, out var location, out _))
            {
                favorites.Add(new Favorite(
                    location,
                    string.IsNullOrWhiteSpace(record.Label) ? location.Name : record.Label));
            }
        }

        return favorites;
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

    // 저장 형식은 Core 의 타입과 분리한다 — LocationId 는 문자열이 아니고,
    // 역직렬화가 곧바로 검증에 걸리면 깨진 파일 하나가 목록 전체를 막는다.
    private sealed record Document
    {
        public List<Record>? Favorites { get; init; }
    }

    /// <summary>경로는 표시형으로 저장한다 — 사람이 열어 고칠 수 있어야 한다.</summary>
    private sealed record Record(string? Path, string? Label = null);
}
