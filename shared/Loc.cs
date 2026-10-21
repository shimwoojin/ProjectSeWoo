using System;
using System.Collections.Generic;
using Godot;

namespace ProjectSeWoo.Shared;

/// <summary>
/// 게임 화면 글자의 언어 (2026-10-21, 영어 지원). 문장 원본은 <c>game/i18n/strings.csv</c> 한 장이다 - 열은
/// <c>keys,ko,en</c>, Godot 이 임포트해서 <see cref="TranslationServer"/> 에 올린다(project.godot <c>internationalization</c>).
///
/// <b>쓰는 법이 둘이다.</b>
/// <list type="bullet">
/// <item>글자가 고정인 라벨·버튼은 <c>Text = "MENU_SHOP"</c> 처럼 <b>키를 그대로 넣는다.</b> Godot 컨트롤은 Text 를 그릴 때
/// 번역하고(auto translate), 언어가 바뀌면 알아서 다시 그린다.</item>
/// <item>숫자·이름이 끼는 글자는 <see cref="F"/> 로 만든 결과를 넣는다. 언어가 바뀌어도 저절로 안 바뀌므로
/// <see cref="Changed"/> 를 듣거나 다음 갱신 때 다시 만든다.</item>
/// </list>
///
/// 키는 대문자·밑줄이다. 친구 이름 같은 남의 글자가 우연히 키와 같아서 번역돼 버리는 일을 막는다.
/// </summary>
public static class Loc
{
    public const string Korean = "ko";
    public const string English = "en";

    /// <summary>언어가 바뀌었다. 키를 넣은 컨트롤은 알아서 바뀌고, <see cref="F"/> 로 만든 글자만 다시 만들면 된다.</summary>
    public static event Action Changed;

    /// <summary>지금 언어 - <see cref="Korean"/> 아니면 <see cref="English"/>.</summary>
    public static string Language => TranslationServer.GetLocale().StartsWith(Korean, StringComparison.Ordinal) ? Korean : English;

    public static string T(string key) => TranslationServer.Translate(key);

    /// <summary><c>string.Format</c> 그대로다 - 숫자 서식(<c>{0:N0}</c>)도 표 안에 쓴다. 어순이 언어마다 달라서다.</summary>
    public static string F(string key, params object[] args) => string.Format(T(key), args);

    /// <summary>
    /// 옵션 값과 스팀 언어로 쓸 언어를 고른다. 옵션이 비어 있으면(자동) 스팀 클라이언트 언어를 따르고, 스팀이 없으면
    /// Windows 언어를 따른다. 한국어가 아니면 전부 영어다 - 표에 두 언어뿐이다.
    /// </summary>
    /// <param name="setting">세이브의 <c>settings.language</c>. 빈 문자열 = 자동.</param>
    /// <param name="steamLanguage"><c>SteamApps.GetCurrentGameLanguage()</c> ("koreana", "english" ...). 스팀이 없으면 null.</param>
    public static string Resolve(string setting, string steamLanguage)
    {
        if (setting is Korean or English)
        {
            return setting;
        }

        if (!string.IsNullOrEmpty(steamLanguage))
        {
            return steamLanguage == "koreana" ? Korean : English;
        }

        return OS.GetLocaleLanguage() == Korean ? Korean : English;
    }

    /// <summary>
    /// 표(strings.csv) 밖에서 오는 문장을 키로 올린다 - 아이템 이름은 items.json 에 ko/en 이 같이 있어서 표에 옮기지
    /// 않는다(<see cref="ItemManifest"/> 가 유일한 원본). 올린 뒤에는 표의 키와 똑같이 쓴다.
    /// </summary>
    public static void AddMessages(IEnumerable<(string Key, string Ko, string En)> messages)
    {
        var ko = new Translation { Locale = Korean };
        var en = new Translation { Locale = English };
        foreach ((string key, string koText, string enText) in messages)
        {
            ko.AddMessage(key, koText);
            en.AddMessage(key, enText);
        }

        TranslationServer.AddTranslation(ko);
        TranslationServer.AddTranslation(en);
    }

    /// <summary>언어를 바꾼다. 같은 언어면 아무것도 안 한다 - <see cref="Changed"/> 를 듣는 쪽이 헛되이 다시 그리지 않게.</summary>
    public static void Apply(string language)
    {
        if (TranslationServer.GetLocale() == language)
        {
            return;
        }

        TranslationServer.SetLocale(language);
        Changed?.Invoke();
    }
}
