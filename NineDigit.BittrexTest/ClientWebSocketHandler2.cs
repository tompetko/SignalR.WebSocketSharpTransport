using Microsoft.AspNet.SignalR.WebSockets;
using System;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NineDigit.BittrexTest
{
    internal class ClientWebSocketHandler2 : WebSocketHandler
    {
        private readonly WebSocketTransport2 _webSocketTransport;

        private readonly PropertyInfo _wsPropInfo;
        private readonly MethodInfo _ProcessWebSocketRequestAsyncMethodInfo;

        public ClientWebSocketHandler2(WebSocketTransport2 webSocketTransport)
            : base(maxIncomingMessageSize: null)
        {
            Debug.Assert(webSocketTransport != null, "webSocketTransport is null");

            _webSocketTransport = webSocketTransport;
            _wsPropInfo = GetWebSocketPropertyInfo();

            _ProcessWebSocketRequestAsyncMethodInfo = this.GetType().BaseType
                .GetMethod(
                    "ProcessWebSocketRequestAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    Type.DefaultBinder,
                    new[] { typeof(WebSocket), typeof(CancellationToken) },
                    null
                );
        }

        private PropertyInfo GetWebSocketPropertyInfo()
        {
            var wsHandlerType = this.GetType();
            var bindingAttrs = BindingFlags.Instance | BindingFlags.NonPublic;
            var wsPropInfo = wsHandlerType.GetProperty("WebSocket", bindingAttrs);

            return wsPropInfo;
        }

        internal WebSocket GetWebSocket()
        {
            var ws = (WebSocket)_wsPropInfo.GetValue(this);

            return ws;
        }

        // for mocking
        internal ClientWebSocketHandler2()
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

        internal Task ProcessWebSocketRequestAsync(WebSocket webSocket, CancellationToken disconnectToken)
        {
            return (Task)_ProcessWebSocketRequestAsyncMethodInfo.Invoke(this, new object[] { webSocket, disconnectToken });
        }
    }
}
