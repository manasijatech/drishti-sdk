using System.Text;

namespace Manasija.Drishti.Http;

/// <summary>Writes a <c>multipart/form-data</c> body into memory.</summary>
internal sealed class MultipartWriter(string boundary)
{
    private readonly MemoryStream _stream = new();

    public string ContentType => "multipart/form-data; boundary=" + boundary;

    public MultipartWriter Field(string name, string value)
    {
        WriteAscii($"--{boundary}\r\nContent-Disposition: form-data; name=\"{Escape(name)}\"\r\n\r\n");
        WriteUtf8(value);
        WriteAscii("\r\n");
        return this;
    }

    public MultipartWriter File(string name, string fileName, string contentType, byte[] content)
    {
        WriteAscii($"--{boundary}\r\nContent-Disposition: form-data; name=\"{Escape(name)}\"; filename=\"");
        WriteUtf8(Escape(fileName));
        WriteAscii($"\"\r\nContent-Type: {contentType}\r\n\r\n");
        _stream.Write(content, 0, content.Length);
        WriteAscii("\r\n");
        return this;
    }

    public byte[] Finish()
    {
        WriteAscii($"--{boundary}--\r\n");
        return _stream.ToArray();
    }

    private static string Escape(string value) => value.Replace("\"", "%22").Replace("\r", "%0D").Replace("\n", "%0A");

    private void WriteAscii(string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        _stream.Write(bytes, 0, bytes.Length);
    }

    private void WriteUtf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        _stream.Write(bytes, 0, bytes.Length);
    }
}
