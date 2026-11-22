using System.IO;
using System.Text;

namespace IPBanManager.Infrastructure;

/// <summary>
/// Gravação durável: força os dados ao disco (FlushFileBuffers) antes de qualquer troca de arquivo.
/// Sem isso, uma queda de energia logo após File.Replace/Move pode deixar o arquivo com o tamanho certo
/// e o conteúdo zerado — foi exatamente o que aconteceu em um teste com reinício abrupto.
/// </summary>
public static class DurableIO
{
    public static void WriteAllText(string path, string content, Encoding encoding)
    {
        var bytes = encoding.GetBytes(content);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }
    }

    /// <summary>Garante em disco um arquivo já escrito por outra API (ex.: File.Copy).</summary>
    public static void FlushToDisk(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            stream.Flush(flushToDisk: true);
    }
}
