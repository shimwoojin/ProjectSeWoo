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
    public const int CurrentVersion = 9;

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
    /// 한 단계씩 올린다(v1 -> v2 -> v3). 버전을 건너뛰는 경로를 만들지 않는 것은,
    /// 단계가 늘어날수록 조합이 폭발해서 안 밟아 본 경로가 생기기 때문이다.
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

                case 2:
                    MigrateV2ToV3(root);
                    version = 3;
                    break;

                case 3:
                    MigrateV3ToV4(root);
                    version = 4;
                    break;

                case 4:
                    MigrateV4ToV5(root);
                    version = 5;
                    break;

                case 5:
                    MigrateV5ToV6(root);
                    version = 6;
                    break;

                case 6:
                    MigrateV6ToV7(root);
                    version = 7;
                    break;

                case 7:
                    MigrateV7ToV8(root);
                    version = 8;
                    break;

                case 8:
                    MigrateV8ToV9(root);
                    version = 9;
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
    /// v2 -> v3 (2026-09-16): <c>settings.cursorIndependent</c> 추가.
    /// "셸을 숨겨도 커서 장식은 남긴다" 옵션이다.
    ///
    /// <see cref="MigrateV1ToV2"/>와 같은 이유로 additive라 버전 태그만 올린다.
    /// 기본값 <c>false</c>가 v2 의 동작(숨기면 커서도 같이 사라진다)과 같으므로,
    /// 구버전 세이브를 들고 온 유저의 체감은 변하지 않는다.
    /// </summary>
    private static void MigrateV2ToV3(JsonNode root)
    {
        root["version"] = 3;
    }

    /// <summary>
    /// v3 -> v4 (2026-09-23, docs/ECONOMY-SERVER.md): <c>bananas</c>, <c>tree</c>,
    /// <c>upgrades</c>, <c>inventory.owned</c> 를 걷어냈다 - 바나나로 산 커서
    /// 장식을 스팀 인벤토리로 옮기고 커뮤니티 마켓 거래를 노리기로 하면서,
    /// 이 값들의 진실이 로컬 세이브에서 서버(<see cref="IEconomyService"/>)/
    /// 스팀(<see cref="IInventoryService"/>)으로 넘어갔다.
    ///
    /// <b>이전 마이그레이션과 방향이 반대다.</b> v1~v3 는 전부 필드를
    /// "추가"했고 additive 라 버전 태그만 올리면 됐다. 이번엔 필드를
    /// "제거"하는데, 그런데도 여기서 값을 지우는 코드가 없는 이유는
    /// <see cref="JsonSerializer"/>가 기본적으로 모르는 프로퍼티를 무시하기
    /// 때문이다 - <c>SaveData</c>에 없는 필드는 역직렬화 시점에 저절로
    /// 버려진다. **출시 전이라 실사용
    /// 세이브가 없으므로** 값을 다른 곳으로 옮기는 마이그레이션(예: 남은
    /// 바나나를 서버 원장에 반영)도 필요 없다고 판단했다.
    /// </summary>
    private static void MigrateV3ToV4(JsonNode root)
    {
        root["version"] = 4;
    }

    /// <summary>
    /// v4 -> v5 (2026-09-24): <c>multi</c> 추가 - 친구 칸 창 위치(<c>friendPositions</c>, B10)와
    /// 마지막 로비(<c>lastLobby</c>, A11). additive 라 버전 태그만 올린다. 비어 있으면 친구 칸은
    /// 기본 자리에 뜨고 자동 재입장은 안 한다 - 멀티를 안 쓰던 유저의 동작 그대로다.
    /// </summary>
    private static void MigrateV4ToV5(JsonNode root)
    {
        root["version"] = 5;
    }

    /// <summary>
    /// v5 -> v6 (2026-09-26): <c>onboardingSeen</c> 추가 - 처음 안내(B15)를 본 판. additive 라 버전 태그만
    /// 올린다. 기본값 0 이라 v5 세이브를 들고 온 사람도 안내를 한 번 본다 - 출시 전이라 그 사람은 개발자뿐이고,
    /// 안내 첫 장이 개인정보 문구라 한 번은 보는 게 맞다.
    /// </summary>
    private static void MigrateV5ToV6(JsonNode root)
    {
        root["version"] = 6;
    }

    /// <summary>
    /// v6 -> v7 (2026-09-26): <c>settings.keystrokeCounting</c> 를 걷어냈다. A6 이 옵션 "타건 수 집계" 체크박스와
    /// 이 필드를 만들었지만 읽는 코드가 끝내 없었다 - 끄기를 눌러도 계속 셌다. 끄면 게임이 멈추는 구조(쳐야
    /// 수확된다)라 동작시키기보다 옵션을 없앴다. 세기를 원하지 않으면 게임을 끈다 - 입력 헬퍼는 게임 프로세스가
    /// 끝나면 같이 끝난다(InputHelper/Program.cs). <see cref="MigrateV3ToV4"/> 와 같은 이유로 지우는 코드는 없다 -
    /// 모르는 필드는 읽을 때 버려진다.
    /// </summary>
    private static void MigrateV6ToV7(JsonNode root)
    {
        root["version"] = 7;
    }

    /// <summary>
    /// v7 -> v8 (2026-09-26): <c>settings.sound</c> 를 걷어냈다 - v1 부터 있던 필드다. 소리를 아예 지원하지 않기로
    /// 했다(B16 취소). 체크박스만 있고 나오는 소리가 하나도 없었다. v7 과 같이 지우는 코드는 없다.
    /// </summary>
    private static void MigrateV7ToV8(JsonNode root)
    {
        root["version"] = 8;
    }

    /// <summary>
    /// v8 -> v9 (2026-09-26, docs/B17-CURSOR-REWORK.md §3-2): 장착 칸이 매달림·잔상·바닥에서 원숭이·바나나·장식으로
    /// 바뀌었다. <b>처음으로 값을 옮기는 마이그레이션이다</b> - 키 이름만 바뀐 게 아니라 두 칸이 한 칸으로 합쳐졌다.
    /// <c>monkey = hang</c>, <c>banana = "banana_01"</c>(기본 지급품), <c>deco = trail ?? base</c> - 둘 다 끼워 뒀으면
    /// 더 눈에 띄는 잔상을 남긴다. 예전 키는 지운다(남겨 두면 다음 저장까지 파일에 두 벌이 보인다).
    /// </summary>
    private static void MigrateV8ToV9(JsonNode root)
    {
        if (root["inventory"]?["equipped"] is JsonObject equipped)
        {
            string hang = equipped["hang"]?.GetValue<string>();
            string trail = equipped["trail"]?.GetValue<string>();
            string @base = equipped["base"]?.GetValue<string>();

            equipped.Remove("hang");
            equipped.Remove("trail");
            equipped.Remove("base");
            equipped["monkey"] = hang ?? "monkey_01";
            equipped["banana"] = "banana_01";
            equipped["deco"] = trail ?? @base;
        }

        root["version"] = 9;
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

        // v4(docs/ECONOMY-SERVER.md) 이후 남은 필드 이름 전부. 하나라도 빠지면
        // 계약 위반이다. bananas/tree/upgrades/inventory.owned 는 v4 에서
        // 서버·스팀으로 옮겨가서 더 이상 여기 없다 (SaveSchema.MigrateV3ToV4).
        string[] required =
        {
            "version", "totalKeystrokes",
            "inventory", "equipped", "monkey", "banana", "deco",
            "settings", "scale", "opacity", "pos", "autostart",
            "positionLocked", "notifications", "cursorEnabled", "hideOnFullscreen",
            "cursorIndependent",
            "multi", "friendPositions", "lastLobby",
            "onboardingSeen",
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

        // lastQuitUtc 가 'Z' 표기로 나가는지. 기획서 예시가 Z 표기이고,
        // DateTimeKind 가 Utc 가 아니면 오프셋 표기로 나가서 계약이 깨진다.
        if (!json.Contains("1970-01-01T00:00:00Z", StringComparison.Ordinal))
        {
            return "lastQuitUtc 가 UTC 'Z' 표기로 직렬화되지 않는다";
        }

        // 마이그레이션 훅이 현재 버전 문서를 그대로 통과시키는지.
        JsonNode node = JsonNode.Parse(json);
        Migrate(node);

        // v8 -> v9 는 값을 옮긴다 (B17). 잔상·바닥을 둘 다 낀 v8 세이브가 원숭이 그대로 + 기본 바나나 + 잔상이 되는지.
        JsonNode v8 = JsonNode.Parse(
            "{\"version\":8,\"inventory\":{\"equipped\":{\"hang\":\"monkey_04\",\"trail\":\"spark_01\",\"base\":\"halo_01\"}}}");
        Migrate(v8);
        JsonNode moved = v8["inventory"]["equipped"];
        if (moved["monkey"]?.GetValue<string>() != "monkey_04" || moved["banana"]?.GetValue<string>() != "banana_01"
            || moved["deco"]?.GetValue<string>() != "spark_01" || moved["hang"] != null)
        {
            return $"v8 -> v9 장착 이동이 틀렸다: {moved.ToJsonString()}";
        }

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
