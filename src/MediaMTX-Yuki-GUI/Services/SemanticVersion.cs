namespace MediaMTX_Yuki_GUI.Services;

/// <summary>轻量语义版本号，只按 major.minor.patch 的数字比较，允许带 v 前缀和 -beta 之类的后缀。</summary>
public readonly record struct SemanticVersion(int Major, int Minor, int Patch)
    : IComparable<SemanticVersion>
{
    /// <summary>解析形如 <c>v1.21.3</c>、<c>0.1.0</c>、<c>8.0-d3</c> 的字符串。</summary>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(text)) return false;

        string body = text.Trim().TrimStart('v', 'V');

        // 后缀（-beta、+build 等）不参与比较，直接截掉
        int cut = body.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) body = body[..cut];

        string[] parts = body.Split('.');
        if (parts.Length < 2) return false;

        if (!int.TryParse(parts[0], out int major)) return false;
        if (!int.TryParse(parts[1], out int minor)) return false;

        int patch = 0;
        if (parts.Length >= 3 && !int.TryParse(parts[2], out patch)) return false;

        version = new SemanticVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result != 0) return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;

        return Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
