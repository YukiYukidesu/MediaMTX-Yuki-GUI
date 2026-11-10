using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 界面文案的多语言表。文案以「键 → 文本」的 JSON 存在 Languages 目录下，
/// 每加一种语言就是多一个文件，代码不用动。
/// </summary>
/// <remarks>
/// 索引器读不到键时回退到中文，中文也没有就把键原样吐出来 —— 这样漏翻的地方
/// 在界面上会很显眼，而不是变成一片空白。
/// </remarks>
public sealed partial class Localization : ObservableObject
{
    /// <summary>界面上的文案都从这里取；XAML 用 {loc:Loc 键名} 绑定到索引器。</summary>
    public static Localization Instance { get; } = new Localization();

    /// <summary>兜底语言：英文没填的键会退回这里。</summary>
    public const string FallbackCulture = "zh-CN";

    private Dictionary<string, string> _current = new(StringComparer.Ordinal);
    private Dictionary<string, string> _fallback = new(StringComparer.Ordinal);

    private Localization()
    {
    }

    /// <summary>当前语言的 BCP-47 标记，例如 zh-CN、en-US。</summary>
    public string Culture { get; private set; } = FallbackCulture;

    /// <summary>语言包所在目录，文件夹里的每个 *.json 都算一种语言。</summary>
    public static string LanguagesDir
        => Path.Combine(AppContext.BaseDirectory, "Languages");

    /// <summary>界面绑定走这里：{Binding [键名], Source={x:Static ...}}。</summary>
    [IndexerName("Item")]
    public string this[string? key]
    {
        get
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (_current.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            if (_fallback.TryGetValue(key, out string? back) && !string.IsNullOrWhiteSpace(back))
            {
                return back;
            }

            return key;
        }
    }

    /// <summary>切到指定语言；语言包缺失就退回中文，不会让界面炸掉。</summary>
    public void SetCulture(string? culture)
    {
        string wanted = string.IsNullOrWhiteSpace(culture) ? FallbackCulture : culture;

        _fallback = ReadFile(FallbackCulture) ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _current = ReadFile(wanted) ?? _fallback;

        Culture = wanted;

        // 索引器整体换了内容，通知一次就够了
        OnPropertyChanged("Item");
        OnPropertyChanged(nameof(Culture));
    }

    /// <summary>语言包里用来描述自身的保留键，翻译时不用管它们。</summary>
    public const string NameKey = "_language_name";
    public const string TranslatedKey = "_translated";

    /// <summary>
    /// 扫描 Languages 目录，返回所有可用的语言包。
    /// 别人把一个 ja-JP.json 丢进去就能被认出来，不需要改代码。
    /// </summary>
    public static IReadOnlyList<LanguageInfo> Available()
    {
        List<LanguageInfo> result = new();

        try
        {
            if (!Directory.Exists(LanguagesDir))
            {
                return result;
            }

            foreach (string file in Directory.GetFiles(LanguagesDir, "*.json"))
            {
                Dictionary<string, string>? map = ReadFile(Path.GetFileNameWithoutExtension(file));

                if (map == null)
                {
                    continue;
                }

                string culture = Path.GetFileNameWithoutExtension(file);

                map.TryGetValue(NameKey, out string? name);
                map.TryGetValue(TranslatedKey, out string? translated);

                bool done = string.Equals(translated, "true", StringComparison.OrdinalIgnoreCase);
                string displayName = string.IsNullOrWhiteSpace(name) ? culture : name;

                if (!done)
                {
                    // 没翻完的语言也照样列出来，只是挂个后缀提醒一下
                    displayName += " · " + Instance["c013-011"];
                }

                result.Add(new LanguageInfo(culture, displayName, done));
            }
        }
        catch (Exception)
        {
            // 目录读不动就当没有语言包，界面会回退到键名
        }

        result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));

        return result;
    }

    /// <summary>当前语言里还有多少条没翻译（值仍是空串）。</summary>
    public int MissingCount
    {
        get
        {
            if (ReferenceEquals(_current, _fallback))
            {
                return 0;
            }

            int missing = 0;

            foreach (KeyValuePair<string, string> pair in _fallback)
            {
                if (pair.Key.StartsWith("_", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!_current.TryGetValue(pair.Key, out string? value) || string.IsNullOrWhiteSpace(value))
                {
                    missing++;
                }
            }

            return missing;
        }
    }

    /// <summary>读某个语言包；文件不存在或格式坏了都返回 null。</summary>
    private static Dictionary<string, string>? ReadFile(string culture)
    {
        try
        {
            string path = Path.Combine(LanguagesDir, culture + ".json");

            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);

            // 语言包按 JSONC 读：每个键值行右边允许跟一条 // 注释，用来标「未人工校对」。
            // 校对完把那行注释删掉即可，不必改值、不必改键名。
            JsonSerializerOptions options = new()
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };

            Dictionary<string, string>? map
                = JsonSerializer.Deserialize<Dictionary<string, string>>(json, options);

            return map == null
                ? null
                : new Dictionary<string, string>(map, StringComparer.Ordinal);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>代码里取文案的简写：L.T("c001-001")。</summary>
    public static string T(string? key)
        => Instance[key];

    /// <summary>系统语言是不是中文，用来决定首启动跟哪种语言。</summary>
    public static string DetectSystemCulture()
    {
        string name = CultureInfo.CurrentUICulture.Name;

        return name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : "en-US";
    }
}

/// <summary>Languages 目录里的一种语言。</summary>
/// <param name="Culture">文件名去掉扩展名，例如 ja-JP。</param>
/// <param name="DisplayName">语言包自己声明的名字，用来显示在下拉里。</param>
/// <param name="Translated">是否已经翻完；没翻完的先不给用户选。</param>
public sealed record LanguageInfo(string Culture, string DisplayName, bool Translated);
