using System;

namespace SignalR.WebSocketSharpTransport.Extensions
{
    internal static class ErrorEventArgsExtensions
    {
        public static Exception ToException(this WebSocketSharp.ErrorEventArgs args)
        {
            return args.Exception ?? new Exception(args.Message);
        }
    }
}
