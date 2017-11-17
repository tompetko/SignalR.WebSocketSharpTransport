using Microsoft.AspNet.SignalR.WebSockets;
using System.Diagnostics;

namespace SignalR.WebSocketSharpTransport
{
    internal class ClientWebSocketHandler : WebSocketHandler
    {
        private readonly WebSocketSharpTransport _webSocketTransport;

        public ClientWebSocketHandler(WebSocketSharpTransport webSocketTransport)
            : base(maxIncomingMessageSize: null)
        {
            Debug.Assert(webSocketTransport != null, "webSocketTransport is null");

            _webSocketTransport = webSocketTransport;
        }

        internal ClientWebSocketHandler()
            : base(maxIncomingMessageSize: null)
        {
        }

        public override void OnMessage(string message)
        {
            _webSocketTransport.OnMessage(message);
        }

        public override void OnOpen()
        {
            _webSocketTransport.OnOpen();
        }

        public override void OnClose()
        {
            _webSocketTransport.OnClose();
        }

        public override void OnError()
        {
            _webSocketTransport.OnError(Error);
        }
    }
}
