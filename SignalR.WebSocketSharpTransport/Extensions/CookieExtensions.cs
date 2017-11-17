using System.Net;

namespace SignalR.WebSocketSharpTransport.Extensions
{
    internal static class CookieExtensions
    {
        public static WebSocketSharp.Net.Cookie ToWebSocketSharpCookie(this Cookie cookie)
        {
            var wssCookie = new WebSocketSharp.Net.Cookie(
                cookie.Name, cookie.Value, cookie.Path, cookie.Domain)
            {
                Comment = cookie.Comment,
                CommentUri = cookie.CommentUri,
                Discard = cookie.Discard,
                Domain = cookie.Domain,
                Expired = cookie.Expired,
                Expires = cookie.Expires,
                HttpOnly = cookie.HttpOnly,
                Name = cookie.Name,
                Path = cookie.Path,
                Port = cookie.Port,
                Secure = cookie.Secure,
                Value = cookie.Value,
                Version = cookie.Version
            };

            return wssCookie;
        }
    }
}
