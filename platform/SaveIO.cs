using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using ProjectSeWoo.Shared;

namespace ProjectSeWoo.Platform;

/// <summary>
/// 세이브 파일 I/O (기획확정-일감분배-260907.md §7-5). 스키마(<see cref="SaveSchema"/>)는
/// 갑/을이 합의한 계약이고, 이 클래스는 그 계약을 디스크에 앉히는 갑의 몫이다.
///
/// A1 문서가 "아직 안 한 것"으로 남겨둔 파일 I/O를 A3에서 채운다 — 창 위치/배율/
/// 투명도(<c>Settings</c>)를 재실행 시 복원하려면 어차피 필요했다.
///
/// **지금은 <c>Settings</c> 필드만 실제로 쓰인다.** 나무/인벤토리 등 나머지는
/// 을의 B5가 게임 레이어를 채우기 전까지 기본값으로 왕복한다. 그래도 부분 필드만
/// 담는 임시 포맷을 만들지 않고 <see cref="SaveData"/> 전체를 왕복시키는 이유는,
/// 임시 포맷을 만들면 B5 때 다시 합쳐야 하기 때문이다.
///
/// 경로는 여전히 Godot 기본 <c>user://</c> 그대로 쓴다 — 실제 디스크 경로는
/// 이 클래스가 아니라 <c>project.godot</c>의 <c>application/config/use_custom_user_dir</c>
/// + <c>custom_user_dir_name="PunchMonkey"</c>가 결정한다 (2026-09-16, §7-5).
/// 그 결과 <c>user://save.json</c>이 <c>%APPDATA%/PunchMonkey/save.json</c>으로
/// 그대로 풀린다. §12의 게임명 최종 확정(스팀 검색 중복 확인 등)은 아직 별개로
/// 남아 있다 — "PunchMonkey"는 지금은 세이브 경로를 정하는 작업명이다. 이름이
/// 또 바뀌면 <c>custom_user_dir_name</c> 한 줄만 고치면 되고, 이 파일은 안 바뀐다.
/// </summary>
public static class SaveIO
{
    private const string SavePath = "user://save.json";

    /// <summary>세이브 파일이 있는가. "첫 실행"과 "저장된 값"을 가르는 데 쓴다.</summary>
    public static bool Exists() => File.Exists(ProjectSettings.GlobalizePath(SavePath));

    /// <summary>
    /// 저장 파일을 읽는다. 없거나 깨졌으면 새 기본값을 돌려준다 — 상주 앱이 세이브
    /// 파일 하나 때문에 못 뜨면 안 된다.
    ///
    /// <b>못 읽은 파일은 덮어쓰기 전에 옆에 보관한다 (A14, 2026-09-24).</b> 기본값으로
    /// 시작하면 10초 뒤 첫 저장이 원본을 덮어써서 누적 타수·설정이 영구히 사라졌다.
    /// 보관본(<c>save.json.broken-날짜</c>)이 있으면 손으로라도 되살릴 수 있다. 그다음
    /// 한 단계 전 세이브(<c>save.json.bak</c>, <see cref="Save"/> 가 남긴다)를 시도한다.
    /// </summary>
    public static SaveData Load()
    {
        string abs = ProjectSettings.GlobalizePath(SavePath);
        if (!File.Exists(abs))
        {
            return new SaveData();
        }

        if (TryRead(abs, out SaveData data, out string error))
        {
            return data;
        }

        string kept = KeepAside(abs, "broken");
        GD.PrintErr($"[save] 로드 실패 ({error}) - 원본을 {Path.GetFileName(kept) ?? "보관 실패"} 로 보관");

        string bak = abs + ".bak";
        if (File.Exists(bak) && TryRead(bak, out data, out error))
        {
            GD.PrintErr("[save] 직전 세이브(.bak)로 복구했다");
            return data;
        }

        GD.PrintErr("[save] 복구할 세이브가 없어 기본값으로 시작");
        return new SaveData();
    }

    /// <summary>파일 하나를 읽어 현재 스키마로 올린다. 예외를 밖으로 내지 않는다.</summary>
    private static bool TryRead(string path, out SaveData data, out string error)
    {
        data = null;
        error = null;
        try
        {
            string json = File.ReadAllText(path);
            JsonNode node = JsonNode.Parse(json) ?? throw new InvalidDataException("빈 세이브 파일");

            // **더 새 버전의 세이브**(베타 브랜치에서 돌아온 경우 등)는 Migrate 가 그대로
            // 통과시키고, 모르는 필드는 역직렬화에서 버려진 채 다음 저장에 덮인다.
            // 읽기는 하되 원본을 먼저 보관해 둔다.
            int version = node["version"]?.GetValue<int>() ?? 0;
            if (version > SaveSchema.CurrentVersion)
            {
                string kept = KeepAside(path, $"v{version}");
                GD.PrintErr($"[save] 이 빌드(v{SaveSchema.CurrentVersion})보다 새 세이브(v{version}) - "
                    + $"아는 필드만 읽는다. 원본은 {Path.GetFileName(kept) ?? "보관 실패"}");
            }

            node = SaveSchema.Migrate(node);
            data = JsonSerializer.Deserialize<SaveData>(node.ToJsonString(), SaveSchema.Options)
                ?? throw new InvalidDataException("역직렬화 결과가 null");
            return true;
        }
        catch (Exception e)
        {
            error = $"{e.GetType().Name}: {e.Message}";
            return false;
        }
    }

    /// <summary>파일을 <c>원래이름.태그-날짜시각</c> 으로 복사해 둔다. 실패하면 null.</summary>
    private static string KeepAside(string path, string tag)
    {
        try
        {
            string kept = $"{path}.{tag}-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(path, kept, overwrite: true);
            return kept;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[save] 보관 복사 실패 ({e.GetType().Name}: {e.Message})");
            return null;
        }
    }

    /// <summary>
    /// 임시 파일에 쓰고 교체한다 (§7-5 "원자적 저장"). 상주 앱은 강제 종료가 잦다 —
    /// 쓰다 만 파일이 직전 정상 세이브를 덮어쓰면 안 된다.
    ///
    /// <b>교체 전에 디스크까지 내린다(<c>Flush(true)</c>).</b> 안 그러면 전원이 나갔을 때
    /// 이름 바꾸기만 기록되고 내용은 안 써진 0바이트 파일이 남을 수 있다. 교체는
    /// <c>File.Replace</c> 로 해서 직전 세이브를 <c>save.json.bak</c> 으로 남긴다 -
    /// <see cref="Load"/> 가 본 파일을 못 읽을 때 쓰는 복구본이다.
    /// </summary>
    public static void Save(SaveData data)
    {
        string abs = ProjectSettings.GlobalizePath(SavePath);
        string dir = Path.GetDirectoryName(abs);
        if (dir != null)
        {
            Directory.CreateDirectory(dir);
        }

        string tmp = abs + ".tmp";
        try
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(data, SaveSchema.Options);
            using (var stream = new FileStream(tmp, FileMode.Create, System.IO.FileAccess.Write, FileShare.None))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(abs))
            {
                File.Replace(tmp, abs, abs + ".bak", ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmp, abs);
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[save] 저장 실패 ({e.GetType().Name}: {e.Message})");
        }
    }
}
