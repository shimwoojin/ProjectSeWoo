using System.Diagnostics;

namespace ProjectSeWoo.Tools;

/// <summary>
/// VS 의 F5 경로에서는 이 코드가 실행되지 않는다.
/// launchSettings.json 의 "Executable" 프로필이 실행 대상을 Godot exe 로 바꿔치기하므로,
/// VS 는 이 어셈블리를 빌드만 하고 실제로는 Godot 을 띄운다.
///
/// `dotnet run --project tools/VsLauncher` 도 같은 프로필을 읽으므로 역시 Main 을 타지 않는다.
/// 즉 평소에 이 Main 은 실행되지 않는다.
///
/// 그래도 Main 을 비워두지 않은 이유:
///   빌드된 tools/VsLauncher/bin/Debug/net8.0/VsLauncher.exe 를 직접 실행하면 이 코드가 돌고,
///   엔진 경로를 외우지 않고도 게임을 띄울 수 있다. 바로가기로 만들어 두면 편하다.
///
/// 만약 VS 에서 F5 를 눌렀는데 아래 경고가 콘솔에 보인다면, launchSettings.json 이
/// 적용되지 않은 것이다. 그 상태로는 Godot 이 자식 프로세스로 떠서 중단점이 걸리지 않는다.
/// </summary>
internal static class Program
{
    /// <summary>환경변수 GODOT 가 없을 때 쓸 경로. 개발자마다 다르므로 GODOT 설정을 권장한다.</summary>
    private const string FallbackGodotExe =
        @"C:\Tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe";

    private static int Main(string[] args)
    {
        if (Debugger.IsAttached)
        {
            Console.WriteLine("[VsLauncher] 경고: 디버거가 이 런처에 붙었다.");
            Console.WriteLine("[VsLauncher] launchSettings.json 의 Executable 프로필이 적용되지 않았다는 뜻이고,");
            Console.WriteLine("[VsLauncher] 이 상태로는 게임이 자식 프로세스로 떠서 중단점이 걸리지 않는다.");
            Console.WriteLine("[VsLauncher] VsLauncher 속성 > 디버그에서 실행 프로필을 확인할 것.");
        }

        string? godot = ResolveGodotExe();
        if (godot is null)
        {
            Console.Error.WriteLine("[VsLauncher] Godot .NET(mono) exe 를 찾을 수 없다.");
            Console.Error.WriteLine($"[VsLauncher] 환경변수 GODOT 를 설정하거나 {nameof(FallbackGodotExe)} 를 고쳐라.");
            Console.Error.WriteLine($"[VsLauncher] 현재 기본값: {FallbackGodotExe}");
            return 1;
        }

        string? projectDir = ResolveProjectDir();
        if (projectDir is null)
        {
            Console.Error.WriteLine("[VsLauncher] 상위 폴더에서 project.godot 을 찾을 수 없다.");
            return 1;
        }

        var psi = new ProcessStartInfo(godot)
        {
            UseShellExecute = false,
            WorkingDirectory = projectDir,
        };
        psi.ArgumentList.Add("--path");
        psi.ArgumentList.Add(projectDir);

        // 추가 인자를 그대로 넘긴다. 예: `VsLauncher.exe -e` → 게임 대신 에디터로 열기
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        Console.WriteLine($"[VsLauncher] {godot}");
        Console.WriteLine($"[VsLauncher] --path {projectDir} {string.Join(' ', args)}");

        using Process? proc = Process.Start(psi);
        if (proc is null)
        {
            Console.Error.WriteLine("[VsLauncher] 프로세스를 시작하지 못했다.");
            return 1;
        }

        proc.WaitForExit();
        return proc.ExitCode;
    }

    private static string? ResolveGodotExe()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("GODOT");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        return File.Exists(FallbackGodotExe) ? FallbackGodotExe : null;
    }

    /// <summary>빌드 출력 위치에서 위로 올라가며 project.godot 을 찾는다.</summary>
    private static string? ResolveProjectDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "project.godot")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
