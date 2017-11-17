using CloudFlareUtilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNet.SignalR.Client.Transports;
using System.Threading;

namespace NineDigit.SignalR.WebSocketSharpTransport.Demo
{
    internal static class BittrexConstants
    {
        public const string HubName = "CoreHub";
        public const string __cfduidCookieName = "__cfduid";
        public const string cf_clearanceCookieName = "cf_clearance";
        public const string accessTokenCookieName = ".AspNet.ApplicationCookie";
        public const string UpdateSummaryStateEventName = "updateSummaryState";
        public const string UpdateExchangeStateEventName = "updateExchangeState";
        public const string UpdateOrderStateEventName = "updateOrderState";
    }

    public sealed class ConnectionConfiguration
    {
        public CookieContainer CookieContainer { get; set; }
        public IDictionary<string, string> Headers { get; set; }
    }

    public sealed class BittrexFeedConnectionConfiguration
    {
        public string AccessToken { get; set; }
        public ConnectionConfiguration Connection { get; set; }

        public static BittrexFeedConnectionConfiguration Default
        {
            get { return new BittrexFeedConnectionConfiguration(); }
        }
    }

    public class BittrexExchange
    {
        private readonly HubConnection _connection;
        private readonly IHubProxy _hubProxy;
        private readonly Uri _feedUri;

        public BittrexExchange(Uri feedUri)
        {
            if (feedUri == null)
                throw new ArgumentNullException(nameof(feedUri));

            _feedUri = feedUri;

            _connection = new HubConnection(feedUri.OriginalString);
            _connection.CookieContainer = new CookieContainer();
            _connection.TraceLevel = TraceLevels.Events;
            _connection.TraceWriter = Console.Out;
            _connection.Closed += Connection_Closed;
            _connection.ConnectionSlow += Connection_ConnectionSlow;
            _connection.Error += Connection_Error;
            _connection.Received += Connection_Received;
            _connection.Reconnected += Connection_Reconnected;
            _connection.Reconnecting += Connection_Reconnecting;
            _connection.StateChanged += Connection_StateChanged;

            _hubProxy = _connection.CreateHubProxy(BittrexConstants.HubName);
            _hubProxy.On<dynamic>(BittrexConstants.UpdateSummaryStateEventName, this.OnUpdateSummaryState);
            _hubProxy.On<dynamic>(BittrexConstants.UpdateExchangeStateEventName, this.OnUpdateExchangeState);
            _hubProxy.On<dynamic>(BittrexConstants.UpdateOrderStateEventName, this.OnUpdateOrderState);
        }

        public HubConnection Connection
        {
            get { return this._connection; }
        }

        public IHubProxy HubProxy
        {
            get { return this._hubProxy; }
        }

        public async Task Connect(BittrexFeedConnectionConfiguration configuration)
        {
            DefaultHttpClientEx httpClient = new DefaultHttpClientEx();
            AutoTransport autoTransport = null;

            if (configuration != null)
            {
                if (configuration.Connection != null)
                {
                    var transports = new IClientTransport[]
                    {
                        new WebSocketSharpTransport(httpClient),
                        new LongPollingTransport(httpClient)
                    };

                    autoTransport = new AutoTransport(httpClient, transports);

                    _connection.CookieContainer = configuration.Connection.CookieContainer;

                    if (configuration.Connection.Headers != null)
                    {
                        foreach (var header in configuration.Connection.Headers)
                            _connection.Headers[header.Key] = header.Value;
                    }

                    _connection.TransportConnectTimeout = new TimeSpan(0, 0, 10);
                }

                if (!string.IsNullOrEmpty(configuration.AccessToken))
                {
                    var aspNetApplicationCookie = new Cookie(BittrexConstants.accessTokenCookieName, configuration.AccessToken, "/", ".bittrex.com");
                    _connection.CookieContainer.Add(_feedUri, aspNetApplicationCookie);
                }
            }

            if (autoTransport == null)
                autoTransport = new AutoTransport(httpClient);
            
            await _connection.Start(autoTransport);
        }

        public void Disconnect()
        {
            Task.Run(() => _connection.Stop());
        }

        private void OnUpdateOrderState(dynamic obj)
        {
            Console.WriteLine("OnUpdateOrderState");
        }

        private void OnUpdateExchangeState(dynamic obj)
        {
            Console.WriteLine("OnUpdateExchangeState");
        }

        private void OnUpdateSummaryState(dynamic obj)
        {
            Console.WriteLine("OnUpdateSummaryState");
        }

        #region event handlers
        private void Connection_StateChanged(StateChange obj)
        {
            Console.WriteLine($"State changed {obj.OldState} -> {obj.NewState}.");
        }

        private void Connection_Reconnecting()
        {
            Console.WriteLine($"Reconnecting.");
        }

        private void Connection_Reconnected()
        {
            Console.WriteLine($"Reconnected.");
        }

        private void Connection_Received(string obj)
        {
            Console.WriteLine($"Received {obj.Length} bytes of data.");
        }

        private void Connection_Error(Exception obj)
        {
            Console.WriteLine($"Error: {obj.Message}.");
        }

        private void Connection_ConnectionSlow()
        {
            Console.WriteLine($"Connection slow.");
        }

        private void Connection_Closed()
        {
            Console.WriteLine($"Connection closed.");
        }
        #endregion
    }

    internal static class HttpMessageHandlerExtensions
    {
        public static HttpMessageHandler GetMostInnerHandler(this HttpMessageHandler self)
        {
            var delegatingHandler = self as DelegatingHandler;
            return delegatingHandler == null ? self : delegatingHandler.InnerHandler.GetMostInnerHandler();
        }
    }

    internal static class CookieCollectionExtensions
    {
        public static Cookie GetCFIdCookie(this CookieCollection self)
        {
            return self[BittrexConstants.__cfduidCookieName];
        }

        public static Cookie GetCFClearanceCookie(this CookieCollection self)
        {
            return self[BittrexConstants.cf_clearanceCookieName];
        }
    }

    internal static class CookieExtensions
    {
        public static string ToHeaderValue(this Cookie cookie)
        {
            return $"{cookie.Name}={cookie.Value}";
        }

        public static IEnumerable<Cookie> GetCookiesByName(this CookieContainer container, Uri uri, params string[] names)
        {
            return container.GetCookies(uri).Cast<Cookie>().Where(c => names.Contains(c.Name)).ToList();
        }
    }
    
    class Program
    {
        static void Main(string[] args)
        {
            const string userAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/61.0.3163.100 Safari/537.36 OPR/48.0.2685.52";

            var bittrexUri = new Uri("https://bittrex.com");
            var bittrexFeedUri = new Uri("https://socket.bittrex.com");

            //

            var feedHeaders = new Dictionary<string, string>();
            var cookieContainer = new CookieContainer();
            var httpClientHandler = new HttpClientHandler()
            {
                UseCookies = true,
                CookieContainer = cookieContainer
            };

            var clearanceHandler = new ClearanceHandler(httpClientHandler);
            var httpClient = new HttpClient(clearanceHandler);

            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

            //

            var connConfig = new ConnectionConfiguration()
            {
                CookieContainer = cookieContainer,
                Headers = feedHeaders
            };

            feedHeaders.Add("User-Agent", userAgent);

            var config = new BittrexFeedConnectionConfiguration()
            {
                // NOTE: Not applicable: AccessToken = "",
                Connection = connConfig
            };

            var exchange = new BittrexExchange(bittrexFeedUri);

            //

            var request = new HttpRequestMessage(HttpMethod.Get, bittrexUri);
            var content = httpClient.SendAsync(request, CancellationToken.None).Result;

            //

            exchange.Connection.CookieContainer = cookieContainer;
            exchange.Connection.StateChanged += (stateChange) =>
            {
                if (stateChange.NewState == ConnectionState.Connected)
                {
                    exchange.HubProxy.Invoke("subscribeToExchangeDeltas", "BTC-EXCL").ContinueWith((t) =>
                    {
                        //bool isConnected = t.Result;
                        //HubProxy.Invoke("QueryExchangeState", "BTC-EXCL");//get order book snapshot
                    });
                }
            };

            exchange.Connect(config).Wait();

            Console.ReadLine();
        }
    }
}
