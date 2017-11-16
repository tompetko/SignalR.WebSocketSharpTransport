using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNet.SignalR.Client.Http;
using Microsoft.AspNet.SignalR.Client.Infrastructure;
using Microsoft.AspNet.SignalR.Client.Transports;
using Microsoft.AspNet.SignalR.WebSockets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;

namespace NineDigit.BittrexTest
{
    internal class DefaultHttpClientWrapper : IHttpClient
    {
        private readonly IHttpClient _httpClient;
        private IConnection _connection;

        public DefaultHttpClientWrapper()
        {
            _httpClient = new DefaultHttpClient();
        }

        public Task<IResponse> Get(string url, Action<IRequest> prepareRequest, bool isLongRunning)
        {
            return _httpClient.Get(url, r => PrepareRequest(prepareRequest, r), isLongRunning);
        }

        public void Initialize(IConnection connection)
        {
            this._connection = connection;

            _httpClient.Initialize(connection);
        }

        public Task<IResponse> Post(string url, Action<IRequest> prepareRequest, IDictionary<string, string> postData, bool isLongRunning)
        {
            return _httpClient.Post(url, r => PrepareRequest(prepareRequest, r), isLongRunning);
        }

        private void PrepareRequest(Action<IRequest> prepareRequest, IRequest request)
        {
            /*
             * Note: Do not call prepareRequest(IRequest) if this.UserAgent property is set.
             * This method sets request.UserAgent property and calls
             * request.SetRequestHeaders(IConnection.Headers).
             * 
             * https://github.com/SignalR/SignalR/blob/d463e0967edb416362c27b58e0a9515cbae698fe/src/Microsoft.AspNet.SignalR.Client/Connection.cs#L915
             */

            //if (string.IsNullOrEmpty(UserAgent))
            //    prepareRequest(request);
            //else
            {
                //request.UserAgent = UserAgent;
                request.SetRequestHeaders(_connection.Headers);
            }
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

    internal static class TaskAsyncHelper
    {
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "This is a shared file")]
        internal static void SetUnwrappedException<T>(this TaskCompletionSource<T> tcs, Exception e)
        {
            var aggregateException = e as AggregateException;
            if (aggregateException != null)
            {
                tcs.SetException(aggregateException.InnerExceptions);
            }
            else
            {
                tcs.SetException(e);
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "This is a shared file")]
        internal static Task FromError(Exception e)
        {
            return FromError<object>(e);
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "This is a shared file")]
        internal static Task<T> FromError<T>(Exception e)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetUnwrappedException<T>(e);
            return tcs.Task;
        }
    }

    internal static class ErrorEventArgsExtensions
    {
        public static Exception ToException(this ErrorEventArgs args)
        {
            return args.Exception ?? new Exception(args.Message);
        }
    }

    internal class ClientWebSocketHandler : WebSocketHandler
    {
        private readonly WebSocketTransportEx _webSocketTransport;

        public ClientWebSocketHandler(WebSocketTransportEx webSocketTransport)
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

    internal static class NetCookieExtensions
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

    internal class WebSocketSharpRequestWrapperEx : IRequest
    {
        private readonly List<KeyValuePair<string, string>> _headers;
        private readonly WebSocket _clientWebSocket;
        private readonly IConnection _connection;

        public WebSocketSharpRequestWrapperEx(WebSocket clientWebSocket, IConnection connection)
        {
            if (clientWebSocket == null)
                throw new ArgumentNullException(nameof(clientWebSocket));

            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            _clientWebSocket = clientWebSocket;
            _connection = connection;

            _headers = new List<KeyValuePair<string, string>>();

            if (clientWebSocket.CustomHeaders != null)
                _headers.AddRange(clientWebSocket.CustomHeaders);

            _clientWebSocket.CustomHeaders = _headers;
            PrepareRequest();
        }

        public string UserAgent
        {
            get { return null; }
            set
            {
            }
        }

        public ICredentials Credentials
        {
            get
            {
                return null;
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public CookieContainer CookieContainer
        {
            get
            {
                return null;
            }
            set
            {
                
                throw new NotImplementedException();
            }
        }

        public void SetCookie(Cookie cookie)
        {
            if (cookie == null)
                throw new ArgumentNullException(nameof(cookie));

            var wssCookie = cookie.ToWebSocketSharpCookie();

            this._clientWebSocket.SetCookie(wssCookie);
        }

        public void SetCredentials(string userName, string password, bool preAuth)
        {
            this._clientWebSocket.SetCredentials(userName, password, preAuth);
        }

        public IWebProxy Proxy
        {
            get
            {
                return null;
            }
            set
            {
            }
        }

        public string Accept
        {
            get
            {
                return null;
            }
            set
            {
            }
        }

        public void SetRequestHeaders(IDictionary<string, string> headers)
        {
            if (headers == null)
            {
                throw new ArgumentNullException(nameof(headers));
            }

            foreach (KeyValuePair<string, string> headerEntry in headers)
            {
                this._headers.Add(
                    new KeyValuePair<string, string>(headerEntry.Key, headerEntry.Value));

                //this.SetHeader(headerEntry.Key, headerEntry.Value);
            }
        }

        public void AddClientCerts(X509CertificateCollection certificates)
        {
            if (certificates == null)
            {
                throw new ArgumentNullException(nameof(certificates));
            }

            _clientWebSocket.SslConfiguration.ClientCertificates = certificates;
        }

        public void Abort()
        {
        }

        /// <summary>
        /// Adds certificates, credentials, proxies and cookies to the request
        /// </summary>
        private void PrepareRequest()
        {
            if (_connection.Certificates != null)
            {
                AddClientCerts(_connection.Certificates);
            }

            if (_connection.CookieContainer != null)
            {
                //CookieContainer = _connection.CookieContainer;
                AddCookies(_connection.CookieContainer, _connection.Url);
            }

            if (_connection.Credentials != null)
            {
                Credentials = _connection.Credentials;
            }

            if (_connection.Proxy != null)
            {
                Proxy = _connection.Proxy;
            }
        }

        private void AddCookies(CookieContainer cookieContainer, string url)
        {
            if (cookieContainer == null)
            {
                throw new ArgumentNullException(nameof(cookieContainer));
            }

            var uri = new Uri(url);
            var cookies = cookieContainer.GetCookies(uri);

            foreach (Cookie cookie in cookies)
            {
                this.SetCookie(cookie);
            }
        }
    }

    public class WebSocketTransportEx : ClientTransportBase
    {
        private CancellationTokenSource _webSocketTokenSource;
        private CancellationToken _disconnectToken;

        private IConnection _connection;
        private string _connectionData;
        private WebSocket _webSocket;
        private int _disposed;

        public WebSocketTransportEx()
            : this(new DefaultHttpClient())
        {
        }

        public WebSocketTransportEx(IHttpClient client)
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
            EventHandler<ErrorEventArgs> onError = null;
            EventHandler onOpen = null;

            CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_webSocketTokenSource.Token, _disconnectToken);
            CancellationToken token = linkedCts.Token;

            var uri = UrlBuilder.ConvertToWebSocketUri(url);
            var wsUrl = uri.OriginalString;

            _connection.Trace(TraceLevels.Events, "WS Connecting to: {0}", uri);

            _webSocket = new WebSocket(wsUrl, new string[0]);
            _webSocket.OnMessage += _webSocket_OnMessage;
            _webSocket.OnOpen += _webSocket_OnOpen;
            _webSocket.OnClose += _webSocket_OnClose;
            _webSocket.OnError += _webSocket_OnError;

            var request = new WebSocketSharpRequestWrapperEx(_webSocket, _connection);

            _connection.PrepareRequest(request);

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

            onError = (o, e) =>
            {
                _webSocket.OnError -= onError;
                tcs.SetException(e.ToException());
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
