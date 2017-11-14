using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNet.SignalR.Client.Http;
using Microsoft.AspNet.SignalR.Client.Infrastructure;
using Microsoft.AspNet.SignalR.Client.Transports;
using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace NineDigit.BittrexTest
{
    internal static class TaskCompletionSourceExtensions
    {
        public static void SetUnwrappedException<T>(this TaskCompletionSource<T> self, Exception ex)
        {
            self.SetException(ex.Unwrap());
        }
    }

    internal static class TaskAsyncHelper
    {
        internal static Task FromError(Exception e)
        {
            return FromError<object>(e);
        }

        internal static Task<T> FromError<T>(Exception e)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetUnwrappedException<T>(e);
            return tcs.Task;
        }
    }

    internal static class ExceptionsExtensions
    {
        internal static Exception Unwrap(this Exception ex)
        {
            if (ex == null)
            {
                return null;
            }

            var next = ex.GetBaseException();
            while (next.InnerException != null)
            {
                // On mono GetBaseException() doesn't seem to do anything
                // so just walk the inner exception chain.
                next = next.InnerException;
            }

            return next;
        }
    }

    internal static class ExceptionHelper
    {
        internal static bool IsRequestAborted(Exception exception)
        {
            exception = exception.Unwrap();

            // Support an alternative way to propagate aborted requests
            if (exception is OperationCanceledException)
            {
                return true;
            }

            // There is a race in StreamExtensions where if the endMethod in ReadAsync is called before
            // the Stream is disposed, but executes after, Stream.EndRead will be called on a disposed object.
            // Since we call HttpWebRequest.Abort in several places while potentially reading the stream,
            // and we don't want to lock around HttpWebRequest.Abort and Stream.EndRead, we just swallow the 
            // exception.
            // If the Stream is closed before the call to the endMethod, we expect an OperationCanceledException,
            // so this is a fairly rare race condition.
            if (exception is ObjectDisposedException)
            {
                return true;
            }

#if !NETSTANDARD
            var webException = exception as WebException;
            return (webException != null && webException.Status == WebExceptionStatus.RequestCanceled);
#else
            return false;
#endif
        }
    }

    public class WebSocketTransport2 : ClientTransportBase
    {
        private readonly ClientWebSocketHandler2 _webSocketHandler;
        private CancellationToken _disconnectToken;
        private IConnection _connection;
        private string _connectionData;
        private CancellationTokenSource _webSocketTokenSource;
        private ClientWebSocket2 _webSocket;
        private int _disposed;

        public WebSocketTransport2()
            : this(new DefaultHttpClient())
        {
        }

        public WebSocketTransport2(IHttpClient client)
            : base(client, "webSockets")
        {
            _disconnectToken = CancellationToken.None;
            ReconnectDelay = TimeSpan.FromSeconds(2);
            _webSocketHandler = new ClientWebSocketHandler2(this);
        }

        // intended for testing

        /// <summary>
        /// The time to wait after a connection drops to try reconnecting.
        /// </summary>
        public TimeSpan ReconnectDelay { get; set; }

        /// <summary>
        /// Indicates whether or not the transport supports keep alive
        /// </summary>
        public override bool SupportsKeepAlive
        {
            get { return true; }
        }

        protected override void OnStart(IConnection connection, string connectionData, CancellationToken disconnectToken)
        {
            _disconnectToken = disconnectToken;
            _connection = connection;
            _connectionData = connectionData;

            // We don't need to await this task
            PerformConnect().ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    TransportFailed(task.Exception);
                }
                else if (task.IsCanceled)
                {
                    TransportFailed(null);
                }
            },
            TaskContinuationOptions.NotOnRanToCompletion);
        }

        // For testing
        public virtual Task PerformConnect()
        {
            return PerformConnect(UrlBuilder.BuildConnect(_connection, Name, _connectionData));
        }

        private async Task PerformConnect(string url)
        {
            var uri = UrlBuilder.ConvertToWebSocketUri(url);

            _connection.Trace(TraceLevels.Events, "WS Connecting to: {0}", uri);

            // TODO: Revisit thread safety of this assignment
            _webSocketTokenSource = new CancellationTokenSource();
            _webSocket = new ClientWebSocket2();

            _connection.PrepareRequest(new WebSocketWrapperRequestEx(_webSocket, _connection));

            CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_webSocketTokenSource.Token, _disconnectToken);
            CancellationToken token = linkedCts.Token;

            await _webSocket.ConnectAsync(uri, token);
            await _webSocketHandler.ProcessWebSocketRequestAsync(_webSocket, token);
        }

        protected override void OnStartFailed()
        {
            // if the transport failed to start we want to stop it silently.
            Dispose();
        }

        public override Task Send(IConnection connection, string data, string connectionData)
        {
            if (connection == null)
            {
                throw new ArgumentNullException("connection");
            }

            // If we don't throw here when the WebSocket isn't open, WebSocketHander.SendAsync will noop.
            if (_webSocketHandler.GetWebSocket().State != WebSocketState.Open)
            {
                // Make this a faulted task and trigger the OnError even to maintain consistency with the HttpBasedTransports
                var ex = new InvalidOperationException("Resources.Error_DataCannotBeSentDuringWebSocketReconnect");
                connection.OnError(ex);
                return TaskAsyncHelper.FromError(ex);
            }

            return _webSocketHandler.Send(data);
        }

        // virtual for testing
        internal virtual void OnMessage(string message)
        {
            _connection.Trace(TraceLevels.Messages, "WS: OnMessage({0})", message);

            ProcessResponse(_connection, message);
        }

        // virtual for testing
        internal virtual void OnOpen()
        {
            // This will noop if we're not in the reconnecting state
            if (_connection.ChangeState(ConnectionState.Reconnecting, ConnectionState.Connected))
            {
                _connection.OnReconnected();
            }
        }

        // virtual for testing
        internal virtual void OnClose()
        {
            _connection.Trace(TraceLevels.Events, "WS: OnClose()");

            if (_disconnectToken.IsCancellationRequested)
            {
                return;
            }

            if (AbortHandler.TryCompleteAbort())
            {
                return;
            }

            DoReconnect();
        }

        // fire and forget
        private async void DoReconnect()
        {
            var reconnectUrl = UrlBuilder.BuildReconnect(_connection, Name, _connectionData);

            while (TransportHelper.VerifyLastActive(_connection) && _connection.EnsureReconnecting())
            {
                try
                {
                    await PerformConnect(reconnectUrl);
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (ExceptionHelper.IsRequestAborted(ex))
                    {
                        break;
                    }

                    _connection.OnError(ex);
                }

                await Task.Delay(ReconnectDelay);
            }
        }

        // virtual for testing
        internal virtual void OnError(Exception error)
        {
            _connection.OnError(error);
        }

        public override void LostConnection(IConnection connection)
        {
            _connection.Trace(TraceLevels.Events, "WS: LostConnection");

            if (_webSocketTokenSource != null)
            {
                _webSocketTokenSource.Cancel();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 1)
                {
                    base.Dispose(disposing);
                    return;
                }

                if (_webSocketTokenSource != null)
                {
                    // Gracefully close the websocket message loop
                    _webSocketTokenSource.Cancel();
                }

                if (_webSocket != null)
                {
                    _webSocket.Dispose();
                }

                if (_webSocketTokenSource != null)
                {
                    _webSocketTokenSource.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }

    /*public class WebSocketTransportEx : WebSocketTransport
    {
        private readonly ClientWebSocketHandlerEx _webSocketHandler;
        private CancellationToken _disconnectToken;
        private IConnection _connection;
        private string _connectionData;

        private readonly FieldInfo __webSocketTokenSourceFieldInfo;
        //private readonly FieldInfo __disconnectTokenFieldInfo;
        private readonly FieldInfo __webSocketFieldInfo;

        private readonly MethodInfo _OnMessageMethodInfo;
        private readonly MethodInfo _OnOpenMethodInfo;
        private readonly MethodInfo _OnCloseMethodInfo;
        private readonly MethodInfo _OnErrorMethodInfo;

        public WebSocketTransportEx()
            : this(new DefaultHttpClient())
        {
        }

        public WebSocketTransportEx(IHttpClient client)
            : base(client)
        {
            _webSocketHandler = new ClientWebSocketHandlerEx(this);

            var baseType = this.GetType().BaseType;

            __webSocketFieldInfo = baseType
                .GetField("_webSocket", BindingFlags.NonPublic | BindingFlags.Instance);

            __webSocketTokenSourceFieldInfo = baseType
                .GetField("_webSocketTokenSource", BindingFlags.NonPublic | BindingFlags.Instance);

            //__disconnectTokenFieldInfo = thisType
            //    .GetField("_disconnectToken", BindingFlags.NonPublic | BindingFlags.Instance);

            _OnMessageMethodInfo = baseType
                .GetMethod("OnMessage", BindingFlags.NonPublic | BindingFlags.Instance);

            _OnOpenMethodInfo = baseType
                .GetMethod("OnOpen", BindingFlags.NonPublic | BindingFlags.Instance);

            _OnCloseMethodInfo = baseType
                .GetMethod("OnClose", BindingFlags.NonPublic | BindingFlags.Instance);

            _OnErrorMethodInfo = baseType
                .GetMethod("OnError", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        protected override void OnStart(IConnection connection, string connectionData, CancellationToken disconnectToken)
        {
            _connection = connection;
            _connectionData = connectionData;
            _disconnectToken = disconnectToken;

            base.OnStart(connection, connectionData, disconnectToken);
        }

        public override Task PerformConnect()
        {
            return PerformConnect(UrlBuilder.BuildConnect(_connection, Name, _connectionData));
        }

        private async Task PerformConnect(string url)
        {
            var uri = UrlBuilder.ConvertToWebSocketUri(url);

            _connection.Trace(TraceLevels.Events, "WS Connecting to: {0}", uri);

            // TODO: Revisit thread safety of this assignment
            CancellationTokenSource wsTokenSource = new CancellationTokenSource();
            ClientWebSocket2 ws = new ClientWebSocket2();

            // TODO: Set via reflection
            __webSocketTokenSourceFieldInfo.SetValue(this, wsTokenSource);
            __webSocketFieldInfo.SetValue(this, ws);

            _connection.PrepareRequest(new WebSocketWrapperRequestEx(ws, _connection));

            CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(wsTokenSource.Token, _disconnectToken);
            CancellationToken token = linkedCts.Token;

            await ws.ConnectAsync(uri, token);
            await _webSocketHandler.ProcessWebSocketRequestAsync(ws, token);
        }

        internal void OnMessage(string message)
        {
            _OnMessageMethodInfo.Invoke(this, new object[] { message });
        }

        internal void OnOpen()
        {
            _OnOpenMethodInfo.Invoke(this, null);
        }

        internal void OnClose()
        {
            _OnCloseMethodInfo.Invoke(this, null);
        }

        internal void OnError(Exception error)
        {
            _OnErrorMethodInfo.Invoke(this, new object[] { error });
        }
    }*/
}
