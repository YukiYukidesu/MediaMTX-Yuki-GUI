using System.Text;

namespace MediaMTX_Yuki_GUI.Services.Rtmp;

/// <summary>
/// AMF0 的值类型标记。RTMP 的命令与元数据都用它序列化。
/// </summary>
public enum Amf0Type : byte
{
    /// <summary>双精度浮点。</summary>
    Number = 0x00,

    /// <summary>布尔。</summary>
    Boolean = 0x01,

    /// <summary>短字符串（≤ 65535 字节）。</summary>
    String = 0x02,

    /// <summary>对象，结尾是一个空串键 + ObjectEnd。</summary>
    Object = 0x03,

    /// <summary>空值。</summary>
    Null = 0x05,

    /// <summary>未定义。</summary>
    Undefined = 0x06,

    /// <summary>ECMA 数组，前面多一个 4 字节元素个数（不可靠，只当提示）。</summary>
    EcmaArray = 0x08,

    /// <summary>对象结束标记，只有 0x00 0x00 0x09 三个字节。</summary>
    ObjectEnd = 0x09,

    /// <summary>严格数组。</summary>
    StrictArray = 0x0A,

    /// <summary>日期，8 字节毫秒 + 2 字节时区。</summary>
    Date = 0x0B,

    /// <summary>长字符串（> 65535 字节）。</summary>
    LongString = 0x0C,
}

/// <summary>
/// AMF0 对象。用字典是因为命令对象里字段顺序无关紧要，而字典读起来最省事。
/// </summary>
public sealed class Amf0Object : Dictionary<string, object?>
{
    /// <summary>建一个空对象。</summary>
    public Amf0Object()
    {
    }

    /// <summary>按键值对建一个对象。</summary>
    /// <param name="pairs">交替的键与值。</param>
    public Amf0Object(params (string Key, object? Value)[] pairs)
    {
        foreach ((string key, object? value) in pairs) this[key] = value;
    }

    /// <summary>取字符串，取不到返回空串。</summary>
    /// <param name="key">键名。</param>
    public string GetString(string key)
    {
        return TryGetValue(key, out object? value) && value is string text ? text : string.Empty;
    }

    /// <summary>取数字，取不到返回 0。</summary>
    /// <param name="key">键名。</param>
    public double GetNumber(string key)
    {
        return TryGetValue(key, out object? value) && value is double number ? number : 0;
    }
}

/// <summary>
/// AMF0 编码器：往一个缓冲区里追加值，最后一次性写出。
/// 不直接写流是因为命令消息要先算出总长度，才能写进 RTMP 的消息头。
/// </summary>
public sealed class Amf0Writer
{
    private readonly List<byte> _buffer = [];

    /// <summary>已经写进去的字节数。</summary>
    public int Length => _buffer.Count;

    /// <summary>取出全部字节。</summary>
    public byte[] ToArray()
    {
        return [.. _buffer];
    }

    /// <summary>写一个字符串（短串与长串自动切换）。</summary>
    /// <param name="value">内容。</param>
    public Amf0Writer WriteString(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);

        if (bytes.Length <= 0xFFFF)
        {
            _buffer.Add((byte)Amf0Type.String);
            _buffer.Add((byte)(bytes.Length >> 8));
            _buffer.Add((byte)(bytes.Length & 0xFF));
        }
        else
        {
            _buffer.Add((byte)Amf0Type.LongString);
            _buffer.Add((byte)(bytes.Length >> 24));
            _buffer.Add((byte)((bytes.Length >> 16) & 0xFF));
            _buffer.Add((byte)((bytes.Length >> 8) & 0xFF));
            _buffer.Add((byte)(bytes.Length & 0xFF));
        }

        _buffer.AddRange(bytes);

        return this;
    }

    /// <summary>写一个数字。</summary>
    /// <param name="value">数值。</param>
    public Amf0Writer WriteNumber(double value)
    {
        _buffer.Add((byte)Amf0Type.Number);

        byte[] bytes = BitConverter.GetBytes(value);

        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);

        _buffer.AddRange(bytes);

        return this;
    }

    /// <summary>写一个布尔。</summary>
    /// <param name="value">真假。</param>
    public Amf0Writer WriteBoolean(bool value)
    {
        _buffer.Add((byte)Amf0Type.Boolean);
        _buffer.Add(value ? (byte)1 : (byte)0);

        return this;
    }

    /// <summary>写一个空值。</summary>
    public Amf0Writer WriteNull()
    {
        _buffer.Add((byte)Amf0Type.Null);

        return this;
    }

    /// <summary>写一个未定义。</summary>
    public Amf0Writer WriteUndefined()
    {
        _buffer.Add((byte)Amf0Type.Undefined);

        return this;
    }

    /// <summary>写一个对象。</summary>
    /// <param name="value">对象内容。</param>
    public Amf0Writer WriteObject(Amf0Object value)
    {
        _buffer.Add((byte)Amf0Type.Object);

        foreach ((string key, object? item) in value)
        {
            WriteKey(key);
            WriteValue(item);
        }

        WriteKey(string.Empty);
        _buffer.Add((byte)Amf0Type.ObjectEnd);

        return this;
    }

    /// <summary>写一个 ECMA 数组（命令里的字段表常用它）。</summary>
    /// <param name="value">对象内容。</param>
    public Amf0Writer WriteEcmaArray(Amf0Object value)
    {
        _buffer.Add((byte)Amf0Type.EcmaArray);

        int count = value.Count;

        _buffer.Add((byte)(count >> 24));
        _buffer.Add((byte)((count >> 16) & 0xFF));
        _buffer.Add((byte)((count >> 8) & 0xFF));
        _buffer.Add((byte)(count & 0xFF));

        foreach ((string key, object? item) in value)
        {
            WriteKey(key);
            WriteValue(item);
        }

        WriteKey(string.Empty);
        _buffer.Add((byte)Amf0Type.ObjectEnd);

        return this;
    }

    /// <summary>按值的实际类型写，认不出来的写成空值。</summary>
    /// <param name="value">任意 AMF0 值。</param>
    public Amf0Writer WriteValue(object? value)
    {
        switch (value)
        {
            case null:
                return WriteNull();
            case string text:
                return WriteString(text);
            case bool flag:
                return WriteBoolean(flag);
            case double number:
                return WriteNumber(number);
            case int number:
                return WriteNumber(number);
            case Amf0Object map:
                return WriteObject(map);
            default:
                return WriteNull();
        }
    }

    private void WriteKey(string key)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(key);

        _buffer.Add((byte)(bytes.Length >> 8));
        _buffer.Add((byte)(bytes.Length & 0xFF));
        _buffer.AddRange(bytes);
    }
}

/// <summary>
/// AMF0 解码器：在消息体上走位，读一个值就把游标往后推。
/// 用类而不是纯函数，是为了让游标天然跟着走。
/// </summary>
public sealed class Amf0Reader
{
    private readonly byte[] _buffer;
    private int _offset;

    /// <summary>包一层缓冲区。</summary>
    /// <param name="buffer">消息体。</param>
    public Amf0Reader(byte[] buffer)
    {
        _buffer = buffer;
    }

    /// <summary>游标是否已经走到结尾。</summary>
    public bool EndOfData => _offset >= _buffer.Length;

    /// <summary>剩余字节数。</summary>
    public int Remaining => _buffer.Length - _offset;

    /// <summary>读一个值，读不出来返回 null。</summary>
    public object? ReadValue()
    {
        if (_offset >= _buffer.Length) return null;

        Amf0Type type = (Amf0Type)_buffer[_offset++];

        switch (type)
        {
            case Amf0Type.Number:
                return ReadNumberValue();

            case Amf0Type.Boolean:
                return _offset < _buffer.Length && _buffer[_offset++] != 0;

            case Amf0Type.String:
                return ReadShortString();

            case Amf0Type.LongString:
                return ReadLongString();

            case Amf0Type.Object:
                return ReadObjectBody();

            case Amf0Type.EcmaArray:
                Skip(4);
                return ReadObjectBody();

            case Amf0Type.StrictArray:
                return ReadStrictArray();

            case Amf0Type.Date:
                Skip(10);
                return null;

            case Amf0Type.Null:
            case Amf0Type.Undefined:
                return null;

            default:
                // 不认识的类型没法确定长度，直接放弃剩下的内容
                _offset = _buffer.Length;
                return null;
        }
    }

    /// <summary>按顺序读若干个值。</summary>
    /// <param name="count">个数。</param>
    public object?[] ReadValues(int count)
    {
        object?[] values = new object?[count];

        for (int i = 0; i < count; i++) values[i] = ReadValue();

        return values;
    }

    // ====== 内部 ======

    private double ReadNumberValue()
    {
        if (_offset + 8 > _buffer.Length)
        {
            _offset = _buffer.Length;
            return 0;
        }

        byte[] bytes = new byte[8];
        Array.Copy(_buffer, _offset, bytes, 0, 8);
        _offset += 8;

        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);

        return BitConverter.ToDouble(bytes, 0);
    }

    private string ReadShortString()
    {
        if (_offset + 2 > _buffer.Length)
        {
            _offset = _buffer.Length;
            return string.Empty;
        }

        int length = (_buffer[_offset] << 8) | _buffer[_offset + 1];
        _offset += 2;

        return ReadUtf8(length);
    }

    private string ReadLongString()
    {
        if (_offset + 4 > _buffer.Length)
        {
            _offset = _buffer.Length;
            return string.Empty;
        }

        int length = (_buffer[_offset] << 24) | (_buffer[_offset + 1] << 16) | (_buffer[_offset + 2] << 8) | _buffer[_offset + 3];
        _offset += 4;

        return ReadUtf8(length);
    }

    private string ReadUtf8(int length)
    {
        if (length <= 0 || _offset + length > _buffer.Length)
        {
            _offset = _buffer.Length;
            return string.Empty;
        }

        string text = Encoding.UTF8.GetString(_buffer, _offset, length);
        _offset += length;

        return text;
    }

    private Amf0Object ReadObjectBody()
    {
        Amf0Object result = new();

        while (_offset < _buffer.Length)
        {
            string key = ReadShortString();

            if (key.Length == 0 && _offset < _buffer.Length && (Amf0Type)_buffer[_offset] == Amf0Type.ObjectEnd)
            {
                _offset++;
                break;
            }

            result[key] = ReadValue();
        }

        return result;
    }

    private List<object?> ReadStrictArray()
    {
        if (_offset + 4 > _buffer.Length)
        {
            _offset = _buffer.Length;
            return [];
        }

        int count = (_buffer[_offset] << 24) | (_buffer[_offset + 1] << 16) | (_buffer[_offset + 2] << 8) | _buffer[_offset + 3];
        _offset += 4;

        List<object?> items = [];

        for (int i = 0; i < count; i++) items.Add(ReadValue());

        return items;
    }

    private void Skip(int count)
    {
        _offset = Math.Min(_offset + count, _buffer.Length);
    }
}
