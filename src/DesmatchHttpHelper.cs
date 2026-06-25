using SPT.Common.Http;

namespace DesmatchMode4
{
    /// <summary>
    /// Обёртка HTTP для SPT 4 (PostJsonAsync возвращает Task).
    /// </summary>
    internal static class DesmatchHttpHelper
    {
        public static string PostJson(string route, string json)
        {
            // SPT PostJson выполняет HTTP в Task.Run — безопасно с главного потока Unity.
            // PostJsonAsync().GetResult() на main thread во время Chainloader вызывает deadlock.
            return RequestHandler.PostJson(route, json);
        }
    }
}
