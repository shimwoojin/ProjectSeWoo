using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 세이브 스키마의 버전과 마이그레이션 (기획확정-일감분배-260907.md §7-5).
///
/// 상주 앱이라 강제 종료가 잦고, 유저가 구버전 세이브를 들고 돌아온다.
/// **버전 필드와 마이그레이션 훅은 v1 부터 있어야 한다** — 나중에 붙이면
/// 버전 없는 파일을 어떻게 읽을지부터 문제가 된다.
/// </summary>
public static class SaveSchema
{
    /// <summary>현재 스키마 버전. 필드를 바꾸면 올리고 마이그레이션을 추가한다.</summary>
    public const int CurrentVersion = 2;

    /// <summary>
    /// 직렬화 옵션. **필드 이름은 어트리뷰트로 고정돼 있으므로 여기서 정하지 않는다.**
    /// 들여쓰기를 켜는 것은 유저가 세이브를 열어봤을 때 읽히게 하기 위해서다 —
    /// 숨길 것이 없다는 게 이 프로젝트의 입장이다 (§7-6).
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// 구버전 JSON 을 현재 버전으로 올린다.
    ///
    /// v1 뿐이라 지금은 할 일이 없지만, **호출 지점을 먼저 만들어 둔다.**
    /// 나중에 스키마를 바꿀 때 로드 경로를 뜯어고치지 않아도 되게 하려는 것이고,
    /// 이게 §7-5 가 "마이그레이션 훅 준비" 라고 쓴 것의 내용이다.
    /// </summary>
    /// <returns>현재 버전으로 올라간 노드.</returns>
    public static JsonNode Migrate(JsonNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        // 버전 필드가 없으면 v1 이전이다. 그런 파일은 출시 전에만 존재하므로
        // 마이그레이션 대상이 아니라 기본값으로 시작하는 게 맞다.
        int version = root["version"]?.GetValue<int>() ?? 0;

        while (version < CurrentVersion)
        {
            switch (version)
            {
                case 1:
                    MigrateV1ToV2(root);
                    version = 2;
                    break;

                default:
                    // 모르는 버전은 조용히 통과시키지 않는다. 세이브가 깨진 채로
                    // 게임이 돌면 유저는 나중에야 알아차린다.
                    throw new NotSupportedException(
                        $"세이브 버전 {version} 에서 {CurrentVersion} 로 올리는 경로가 없다");
            }
        }

        return root;
    }

    /// <summary>
    /// v1 -> v2 (A6, 2026-09-16): <c>settings</c>에 §7-4 옵션 5개(위치 잠금/알림/
    /// 커서 장식/전체화면 위 숨김/타건 카운트)를 추가했다.
    ///
    /// 전부 additive라 필드가 없어도 <see cref="SaveData.SettingsState"/>의 C# 기본값이
    /// 자동으로 채워진다 - 이 메서드가 실제로 하는 일은 <c>version</c> 태그를 올리는
    /// 것뿐이다. 그래도 이 메서드가 필요한 이유는, 안 올리면 이 세이브가 다음에도
    /// 계속 "구버전"으로 보여서 매번 이 switch 를 다시 타기 때문이다.
    /// </summary>
    private static void MigrateV1ToV2(JsonNode root)
    {
        root["version"] = 2;
    }

    /// <summary>
    /// 스키마가 기획서 §7-5 의 JSON 과 실제로 맞는지 확인한다.
    ///
    /// 계약 문서와 코드가 갈라지는 것은 눈으로는 안 잡힌다. 필드 하나가
    /// growth_ms 로 나가도 컴파일은 통과하고, 을이 구현을 끝낸 뒤에야 드러난다.
    /// 그래서 왕복 직렬화로 자동 확인한다. <c>--selftest</c> 인자로 실행된다.
    /// </summary>
    /// <returns>문제가 없으면 null, 있으면 사람이 읽을 실패 사유.</returns>
    public static string SelfTest()
    {
        var fresh = new SaveData();
        string json = JsonSerializer.Serialize(fresh, Options);

        SaveData back = JsonSerializer.Deserialize<SaveData>(json, Options);
        if (back == null)
        {
            return "역직렬화 결과가 null 이다";
        }

        // 기획서 §7-5 에 실린 필드 이름 전부. 하나라도 빠지면 계약 위반이다.
        string[] required =
        {
            "version", "bananas", "totalKeystrokes",
            "tree", "slots", "growthMs", "slotTimers",
            "upgrades", "power", "cycle",
            "inventory", "owned", "equipped", "hang", "trail", "base",
            "settings", "scale", "opacity", "pos", "sound", "autostart",
            "positionLocked", "notifications", "cursorEnabled", "hideOnFullscreen", "keystrokeCounting",
            "lastQuitUtc",
        };

        foreach (string name in required)
        {
            if (!json.Contains($"\"{name}\":", StringComparison.Ordinal))
            {
                return $"필드 '{name}' 이 JSON 에 없다. 기획서 §7-5 와 어긋난다";
            }
        }

        if (back.Version != CurrentVersion)
        {
            return $"version 이 {back.Version} 로 돌아왔다. {CurrentVersion} 이어야 한다";
        }

        if (back.Tree.Slots != back.Tree.SlotTimers.Length)
        {
            return $"slots({back.Tree.Slots}) 와 slotTimers 길이({back.Tree.SlotTimers.Length})가 다르다";
        }

        // lastQuitUtc 가 'Z' 표기로 나가는지. 기획서 예시가 Z 표기이고,
        // DateTimeKind 가 Utc 가 아니면 오프셋 표기로 나가서 계약이 깨진다.
        if (!json.Contains("1970-01-01T00:00:00Z", StringComparison.Ordinal))
        {
            return "lastQuitUtc 가 UTC 'Z' 표기로 직렬화되지 않는다";
        }

        // 마이그레이션 훅이 현재 버전 문서를 그대로 통과시키는지.
        JsonNode node = JsonNode.Parse(json);
        Migrate(node);

        return null;
    }

    /// <summary>SelfTest 결과를 사람이 읽을 여러 줄로 만든다. 콘솔 출력용.</summary>
    public static string Describe()
    {
        string fail = SelfTest();
        string json = JsonSerializer.Serialize(new SaveData(), Options);

        return fail == null
            ? $"[save] 스키마 v{CurrentVersion} self-test PASS\n{json}"
            : $"[save] 스키마 v{CurrentVersion} self-test FAIL: {fail}\n{json}";
    }
}
