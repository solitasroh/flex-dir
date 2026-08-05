using System.Diagnostics;
using System.Runtime.InteropServices;

using FlexDir.Core.Locations;
using FlexDir.Core.Presentation;
using FlexDir.Shell.Presentation;

namespace FlexDir.Probe;

/// <summary>
/// 자동 테스트가 닿지 않는 자리를 손으로 확인하는 도구 (ADR-015).
/// 결과는 <c>.harness/manual-plan.md</c> 의 사람 확인 항목에 적는다.
/// </summary>
internal static partial class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var command = args.Length > 0 ? args[0] : "help";

        switch (command)
        {
            case "typeicons":
                await TypeIconsAsync();

                return 0;

            case "thumbnail":
                await ThumbnailAsync(args.Length > 1 ? args[1] : string.Empty);

                return 0;

            case "leak":
                await LeakAsync(args.Length > 1 ? args[1] : @"C:\Windows\System32");

                return 0;

            default:
                Console.WriteLine("""
                    사용법: dotnet run --project .harness/probe -- <command>

                      typeicons            확장자·크기별 형식 아이콘 크기를 찍는다
                      thumbnail <path>     한 파일의 썸네일을 뽑아 본다
                      leak [folder]        폴더를 반복 훑으며 GDI·USER 핸들 수를 관찰한다
                    """);

                return 1;
        }
    }

    /// <summary>
    /// 요청 크기가 실제로 다른 이미지 리스트로 갈리는지 본다. SHGFI_ICON 만 쓰면 96 을
    /// 물어도 32 가 오므로, 여기서 96 줄이 32 줄과 같으면 이미지 리스트 경로가 죽은 것이다.
    /// </summary>
    private static async Task TypeIconsAsync()
    {
        using var source = new ShellThumbnailSource();

        string[] extensions = ["txt", "exe", "pdf", "jpg", "zzzzz", ""];
        int[] sizes = [16, 32, 48, 96];

        Console.WriteLine("확장자   크기   결과");

        foreach (var extension in extensions)
        {
            foreach (var size in sizes)
            {
                var isDirectory = extension.Length == 0;
                var icon = await source.GetTypeIconAsync(extension, isDirectory, size, CancellationToken.None);

                Console.WriteLine(
                    $"{(isDirectory ? "<폴더>" : extension),-8} {size,4}   {Describe(icon)}");
            }
        }
    }

    private static async Task ThumbnailAsync(string path)
    {
        if (!LocationId.TryParse(path, out var location, out var error))
        {
            Console.WriteLine($"경로를 읽을 수 없다: {error}");

            return;
        }

        using var source = new ShellThumbnailSource();

        var watch = Stopwatch.StartNew();
        var thumbnail = await source.GetThumbnailAsync(location, 96, CancellationToken.None);

        watch.Stop();

        Console.WriteLine($"{location.DisplayPath}");
        Console.WriteLine($"  96px 요청 → {Describe(thumbnail)}  ({watch.ElapsedMilliseconds}ms)");

        if (thumbnail is null)
        {
            Console.WriteLine("  null 이다 — SIIGBF_THUMBNAILONLY 가 붙어 있으면 정상 결과일 수 있다.");
        }
    }

    /// <summary>
    /// GDI 누수 확인. 핸들은 StaWorkQueue 작업 안에서 만들고 그 안에서 해제하기로 했고
    /// (docs/SHELL_NOTES.md §아이콘 함정 3), 그게 지켜졌다면 반복해도 수가 정착해야 한다.
    /// </summary>
    private static async Task LeakAsync(string folder)
    {
        if (!LocationId.TryParse(folder, out var location, out _))
        {
            Console.WriteLine($"경로를 읽을 수 없다: {folder}");

            return;
        }

        var files = Directory.GetFiles(location.DisplayPath).Take(200).ToArray();

        if (files.Length == 0)
        {
            Console.WriteLine($"파일이 없다: {location.DisplayPath}");

            return;
        }

        using var source = new ShellThumbnailSource();

        Console.WriteLine($"{location.DisplayPath} · 파일 {files.Length}개 · 회차마다 아이콘+썸네일 전부 요청");
        Console.WriteLine("회차   GDI    USER   관리 힙(MB)");

        for (var round = 1; round <= 10; round++)
        {
            foreach (var file in files)
            {
                if (!LocationId.TryParse(file, out var item, out _))
                {
                    continue;
                }

                var extension = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();

                await source.GetTypeIconAsync(extension, false, 96, CancellationToken.None);
                await source.GetThumbnailAsync(item, 96, CancellationToken.None);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var process = GetCurrentProcess();

            Console.WriteLine(
                $"{round,4}   {GetGuiResources(process, GrGdiObjects),-6} " +
                $"{GetGuiResources(process, GrUserObjects),-6} " +
                $"{GC.GetTotalMemory(false) / (1024.0 * 1024.0):F1}");
        }

        Console.WriteLine();
        Console.WriteLine("GDI 수가 회차마다 늘면 누수다. 초반 상승 뒤 정착하면 shell 캐시가 자리를 잡은 것이다.");
    }

    private static string Describe(ThumbnailBitmap? bitmap)
        => bitmap is null
            ? "null"
            : $"{bitmap.Width}×{bitmap.Height}  {bitmap.Pixels.Length / 1024.0:F1}KB  알파 {Alpha(bitmap)}";

    /// <summary>
    /// 알파가 전부 0 이면 화면에서 보이지 않는다. 변환층이 마스크 합성을 놓친 신호다.
    /// </summary>
    private static string Alpha(ThumbnailBitmap bitmap)
    {
        var opaque = 0;
        var partial = 0;

        for (var index = 3; index < bitmap.Pixels.Length; index += 4)
        {
            if (bitmap.Pixels[index] == 0xFF)
            {
                opaque++;
            }
            else if (bitmap.Pixels[index] != 0)
            {
                partial++;
            }
        }

        return $"불투명 {opaque} · 반투명 {partial}";
    }

    private const uint GrGdiObjects = 0;
    private const uint GrUserObjects = 1;

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("user32.dll")]
    private static partial uint GetGuiResources(nint process, uint flags);
}
