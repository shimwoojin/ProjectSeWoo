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
    /// 파일 하나 때문에 못 뜨면 안 된다. 실패 사유는 로그로만 남긴다.
    /// </summary>
    public static SaveData Load()
    {
        string abs = ProjectSettings.GlobalizePath(SavePath);
        if (!File.Exists(abs))
        {
            return new SaveData();
        }

        try
        {
            string json = File.ReadAllText(abs);
            JsonNode node = JsonNode.Parse(json) ?? throw new InvalidDataException("빈 세이브 파일");
            node = SaveSchema.Migrate(node);

            SaveData data = JsonSerializer.Deserialize<SaveData>(node.ToJsonString(), SaveSchema.Options);
            return data ?? new SaveData();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[save] 로드 실패, 기본값으로 시작 ({e.GetType().Name}: {e.Message})");
            return new SaveData();
        }
    }

    /// <summary>
    /// 임시 파일에 쓰고 교체한다 (§7-5 "원자적 저장"). 상주 앱은 강제 종료가 잦다 —
    /// 쓰다 만 파일이 직전 정상 세이브를 덮어쓰면 안 된다.
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
            string json = JsonSerializer.Serialize(data, SaveSchema.Options);
            File.WriteAllText(tmp, json);
            File.Move(tmp, abs, overwrite: true);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[save] 저장 실패 ({e.GetType().Name}: {e.Message})");
        }
    }
}
