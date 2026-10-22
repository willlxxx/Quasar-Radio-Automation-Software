using System.IO;
using System.Net.Sockets;

namespace quasar_radio;

public static class UserErrorMessages
{
    public static string For(Exception exception, string operation) => exception switch
    {
        UnauthorizedAccessException => $"No tienes permisos para {operation}. Revisa los permisos de la carpeta o dispositivo.",
        FileNotFoundException => $"No se encontró el archivo necesario para {operation}.",
        DirectoryNotFoundException => $"No se encontró la carpeta necesaria para {operation}.",
        IOException => $"No se pudo acceder al archivo durante la operación: {operation}.",
        NotSupportedException => $"El formato o dispositivo no es compatible con la operación: {operation}.",
        InvalidDataException => $"Los datos necesarios para {operation} están dañados o no son válidos.",
        SocketException or TimeoutException => $"La conexión de red necesaria para {operation} no respondió. Se volverá a intentar automáticamente.",
        _ => $"No se pudo completar la operación: {operation}. Revisa el registro para más detalles."
    };
}