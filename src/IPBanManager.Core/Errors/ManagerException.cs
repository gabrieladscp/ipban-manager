using System;
using System.IO;
using System.Xml;

namespace IPBanManager.Core.Errors;

/// <summary>Erro esperado, com mensagem já amigável para o usuário final.</summary>
public class ManagerException : Exception
{
    public ManagerException(string message) : base(message) { }
    public ManagerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Converte exceções técnicas em avisos compreensíveis.</summary>
public static class ErrorMessages
{
    public static string Translate(Exception ex)
    {
        switch (ex)
        {
            case ManagerException m:
                return m.Message;
            case UnauthorizedAccessException _:
                return "Acesso negado. Execute o IPBan Manager como Administrador e verifique as permissões da pasta do IPBan.";
            case FileNotFoundException _:
            case DirectoryNotFoundException _:
                return "Arquivo ou pasta do IPBan não encontrado. Verifique a pasta de instalação no topo da janela.";
            case IOException _:
                return "O arquivo está em uso por outro processo. Aguarde alguns segundos e tente novamente.";
            case XmlException x:
                return "O arquivo ipban.config não é um XML válido: " + x.Message;
            default:
                return "Ocorreu um erro inesperado: " + ex.Message + "\n\nOs detalhes foram gravados no log do aplicativo.";
        }
    }
}
