using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNet.SignalR.Client.Http;
using Microsoft.AspNet.SignalR.Client.Infrastructure;
using Microsoft.AspNet.SignalR.Client.Transports;
using NineDigit.SignalR.WebSocketSharpTransport.Extensions;
using NineDigit.SignalR.WebSocketSharpTransport.Helpers;
using System;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;

namespace NineDigit.SignalR.WebSocketSharpTransport
{
    public class WebSocketSharpTransport : ClientTransportBase
    {
        private CancellationTokenSource _webSocketTokenSource;
        private CancellationToken _disconnectToken;

        private IConnection _connection;
        private string _connectionData;
        private WebSocket _webSocket;
        private int _disposed;

        public WebSocketSharpTransport()
            : this(new DefaultHttpClientEx())
        {
        }

        public WebSocketSharpTransport(IHttpClient client)
            : base(client, "webSockets")
        {
            ReconnectDelay = TimeSpan.FromSeconds(2);
            _webSocketTokenSource = new CancellationTokenSource();
        }

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
            _connection = connection;
            _connectionData = connectionData;
            _disconnectToken = disconnectToken;

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

        public virtual async Task PerformConnect()
        {
            await PerformConnect(UrlBuilder.BuildConnect(_connection, Name, _connectionData));
        }

        private Task PerformConnect(string url)
        {
            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
            Uri uri = UrlBuilder.ConvertToWebSocketUri(url);
            string wsUrl = uri.OriginalString;

            EventHandler<ErrorEventArgs> onError = null;
            EventHandler onOpen = null;

            _connection.Trace(TraceLevels.Events, "WS Connecting to: {0}", uri);

            // TODO: Revisit thread safety of this assignment
            _webSocketTokenSource = new CancellationTokenSource();
            _webSocket = new WebSocket(wsUrl, new string[0]);

            _webSocket.OnMessage += _webSocket_OnMessage;
            _webSocket.OnOpen += _webSocket_OnOpen;
            _webSocket.OnClose += _webSocket_OnClose;
            _webSocket.OnError += _webSocket_OnError;

            var request = new WebSocketSharpRequestWrapperEx(_webSocket, _connection);
            _connection.PrepareRequest(request);

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_webSocketTokenSource.Token, _disconnectToken);
            var token = linkedCts.Token;

            token.Register(() => tcs.TrySetCanceled());

            onError = (o, e) =>
            {
                _webSocket.OnError -= onError;
                _webSocket.OnOpen -= onOpen;

                tcs.SetException(e.ToException());
            };

            onOpen = (o, e) =>
            {
                _webSocket.OnOpen -= onOpen;
                _webSocket.OnError -= onError;

                tcs.SetResult(null);
            };

            _webSocket.OnError += onError;
            _webSocket.OnOpen += onOpen;

            _webSocket.ConnectAsync();

            return tcs.Task;
        }

        private void _webSocket_OnError(object sender, ErrorEventArgs e)
        {
            this.OnError(e.ToException());
        }

        private void _webSocket_OnClose(object sender, CloseEventArgs e)
        {
            this.OnClose();
        }

        private void _webSocket_OnOpen(object sender, EventArgs e)
        {
            this.OnOpen();
        }

        private void _webSocket_OnMessage(object sender, MessageEventArgs e)
        {
            if (e.IsText)
            {
                this.OnMessage(e.Data);
            }
            else
            {
                throw new NotImplementedException();
            }
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
                throw new ArgumentNullException(nameof(connection));
            }

            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
            EventHandler<ErrorEventArgs> onError = null;
            Action<bool> onSent = null;

            // If we don't throw here when the WebSocket isn't open, WebSocketHander.SendAsync will noop.
            if (_webSocket.ReadyState != WebSocketState.Open)
            {
                // Make this a faulted task and trigger the OnError even to maintain consistency with the HttpBasedTransports
                var ex = new InvalidOperationException("Data can not be sent during WebSocket reconnect.");
                var result = TaskAsyncHelper.FromError(ex);

                connection.OnError(ex);
                return result;
            }

            onError = (o, args) =>
            {
                _webSocket.OnError -= onError;
                tcs.SetException(args.ToException());
            };

            onSent = (sent) =>
            {
                _webSocket.OnError -= onError;

                if (sent)
                {
                    tcs.SetResult(null);
                }

                // onError handler is invoked, when sent == false
            };

            _webSocket.OnError += onError;
            _webSocket.SendAsync(data, onSent);

            return tcs.Task;
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
            _webSocketTokenSource?.Cancel();
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

                // Gracefully close the websocket message loop
                _webSocketTokenSource?.Cancel();

                //_webSocket?.Dispose();

                _webSocketTokenSource?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
