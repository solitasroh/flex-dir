using System.Diagnostics;
using System.Runtime.InteropServices;

using FlexDir.Core.Errors;
using FlexDir.Core.Locations;
using FlexDir.Core.Presentation;
using FlexDir.Shell.Activation;
using FlexDir.Shell.Operations;
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

            case "activate":
                return await ActivateAsync(args.Length > 1 ? args[1] : string.Empty);

            case "recycle":
                return await RecycleAsync();

            case "clipboard":
                return Clipboard(args.Length > 1 ? args[1] : "paste", args.Skip(2).ToArray());

            default:
                Console.WriteLine("""
                    사용법: dotnet run --project .harness/probe -- <command>

                      typeicons                    확장자·크기별 형식 아이콘 크기를 찍는다
                      thumbnail <path>             한 파일의 썸네일을 뽑아 본다
                      leak [folder]                폴더를 반복 훑으며 GDI·USER 핸들 수를 관찰한다
                      activate <path>              연결 프로그램을 실제로 띄운다
                      recycle                      임시 파일을 만들어 휴지통으로 보낸다
                      clipboard copy <path>...     실제 클립보드에 복사로 싣는다
                      clipboard cut <path>...      실제 클립보드에 잘라내기로 싣는다
                      clipboard paste              실제 클립보드에 있는 것을 읽는다

                    activate 는 프로그램을 띄우고, recycle 은 휴지통에 항목을 남기며,
                    clipboard 는 사용자의 클립보드를 덮어쓴다. 자동 테스트가 하지 않는
                    일이라 여기 있다.
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

    /// <summary>
    /// 연결 프로그램이 정말로 뜨는지. 자동 테스트는 실행 지점을 바꿔 끼우므로 여기까지
    /// 오지 못한다 — 프로세스를 띄우거나 shell 오류 대화상자를 부르기 때문이다.
    /// </summary>
    private static async Task<int> ActivateAsync(string path)
    {
        if (!LocationId.TryParse(path, out var location, out var error))
        {
            Console.WriteLine($"경로를 읽을 수 없다: {error}");

            return 1;
        }

        using var activator = new ShellItemActivator();

        Console.WriteLine($"{location.DisplayPath} 를 연다.");

        var watch = Stopwatch.StartNew();

        try
        {
            await activator.ActivateAsync(location, CancellationToken.None);

            watch.Stop();

            Console.WriteLine($"  돌아왔다 ({watch.ElapsedMilliseconds}ms). 프로그램이 떴는지 눈으로 본다.");
            Console.WriteLine("  연결 프로그램이 없으면 shell 대화상자가 뜨고, 그것을 닫아도 오류가 아니다.");

            return 0;
        }
        catch (LocationAccessException failure)
        {
            watch.Stop();

            Console.WriteLine($"  실패: {failure.Kind} (Win32 {failure.Win32Error}) — {failure.Message}");

            return 1;
        }
    }

    /// <summary>
    /// <b>정말 휴지통에 들어가는가.</b> 이것이 <c>FOFX_RECYCLEONDELETE</c> 를 확인하는
    /// 유일한 방법이다 — 자동 테스트는 플래그가 실렸는지까지만 본다.
    /// </summary>
    private static async Task<int> RecycleAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "flex-dir-probe");

        Directory.CreateDirectory(folder);

        var name = $"휴지통확인-{DateTime.Now:HHmmss}.txt";
        var path = Path.Combine(folder, name);

        await File.WriteAllTextAsync(path, "flex-dir 프로브가 만든 파일이다. 지워도 된다.");

        if (!LocationId.TryParse(path, out var location, out _))
        {
            Console.WriteLine($"경로를 읽을 수 없다: {path}");

            return 1;
        }

        var before = RecycleBinEntries();

        using var operations = new ShellFileOperations();

        Console.WriteLine($"{path}");

        try
        {
            await operations.DeleteAsync([location], CancellationToken.None);
        }
        catch (LocationAccessException failure)
        {
            Console.WriteLine($"  실패: {failure.Kind} (Win32 {failure.Win32Error})");

            return 1;
        }

        Console.WriteLine($"  파일이 남아 있는가: {File.Exists(path)}");

        var after = RecycleBinEntries();

        if (before is null || after is null)
        {
            Console.WriteLine("  휴지통 폴더를 읽지 못했다. 휴지통을 열어 눈으로 확인한다.");
        }
        else
        {
            var added = after.Except(before).ToArray();

            Console.WriteLine($"  휴지통에 늘어난 항목: {added.Length}개");

            foreach (var entry in added)
            {
                Console.WriteLine($"    {entry}");
            }
        }

        Console.WriteLine($"  휴지통을 열어 '{name}' 이 있는지 본다. 없으면 영구 삭제된 것이다.");

        return 0;
    }

    /// <summary>
    /// <c>$Recycle.Bin</c> 아래에서 읽을 수 있는 항목의 이름. 권한이 없으면 <c>null</c> 이다 —
    /// 그때는 사람이 휴지통을 연다.
    /// </summary>
    private static HashSet<string>? RecycleBinEntries()
    {
        var root = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "$Recycle.Bin");

        try
        {
            var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var user in Directory.GetDirectories(root))
            {
                try
                {
                    // 이름이 $I/$R 로 시작하는 쌍이 항목 하나다. 이름만 세면 충분하다.
                    entries.UnionWith(Directory.GetFileSystemEntries(user).Select(Path.GetFileName)!);
                }
                catch (UnauthorizedAccessException)
                {
                    // 다른 사용자의 휴지통이다.
                }
            }

            return entries;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// <b>탐색기와 실제로 주고받는가.</b> 자동 테스트는 클립보드에 닿는 자리를 바꿔 끼우므로
    /// 바이트 배치까지만 본다 — 그 바이트를 탐색기가 받아들이는지는 여기서만 확인된다.
    /// </summary>
    private static int Clipboard(string mode, string[] paths)
    {
        using var clipboard = new ShellClipboardBridge();

        if (mode == "paste")
        {
            if (!clipboard.TryGetPaste(out var items, out var isMove))
            {
                Console.WriteLine("클립보드에 붙여넣을 것이 없다.");

                return 0;
            }

            Console.WriteLine($"{(isMove ? "잘라내기" : "복사")} · {items.Count}개");

            foreach (var item in items)
            {
                Console.WriteLine($"  {item.DisplayPath}");
            }

            return 0;
        }

        if (mode is not ("copy" or "cut"))
        {
            Console.WriteLine($"모르는 모드다: {mode} (copy · cut · paste)");

            return 1;
        }

        var locations = new List<LocationId>();

        foreach (var path in paths)
        {
            if (LocationId.TryParse(path, out var location, out var error))
            {
                locations.Add(location);
            }
            else
            {
                Console.WriteLine($"경로를 읽을 수 없다: {path} ({error})");
            }
        }

        if (locations.Count == 0)
        {
            Console.WriteLine("실을 것이 없다.");

            return 1;
        }

        if (mode == "copy")
        {
            clipboard.SetCopy(locations);
        }
        else
        {
            clipboard.SetCut(locations);
        }

        Console.WriteLine($"클립보드에 {mode} 로 {locations.Count}개를 실었다. 탐색기에서 Ctrl+V 로 확인한다.");

        return 0;
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
