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
using System.Collections.ObjectModel;

namespace NineDigit.BittrexTest
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

    public class CloudFlareConnectConfiguration
    {
        public IEnumerable<Cookie> Cookies { get; set; }
        public string UserAgent { get; set; }
    }

    public sealed class BittrexFeedConnectConfiguration
    {
        public CloudFlareConnectConfiguration CloudFlare { get; set; }
        public string AccessToken { get; set; }

        public static BittrexFeedConnectConfiguration Default
        {
            get { return new BittrexFeedConnectConfiguration(); }
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

        public async Task Connect(BittrexFeedConnectConfiguration configuration)
        {
            DefaultHttpClientWrapper httpClient = new DefaultHttpClientWrapper();
            AutoTransport autoTransport = null;

            if (configuration != null)
            {
                if (configuration.CloudFlare != null)
                {
                    var transports = new IClientTransport[]
                    {
                        new WebSocketTransportEx(httpClient),
                        new LongPollingTransport(httpClient)
                    };

                    autoTransport = new AutoTransport(
                        httpClient, transports);

                    _connection.Headers.Add("User-Agent", configuration.CloudFlare.UserAgent);

                    foreach (var cookie in configuration.CloudFlare.Cookies)
                    {
                        _connection.CookieContainer.Add(_feedUri, new Cookie(cookie.Name, cookie.Value));
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
        
        private void OnUpdateOrderState(dynamic obj)
        {
        }

        private void OnUpdateExchangeState(dynamic obj)
        {
        }

        private void OnUpdateSummaryState(dynamic obj)
        {
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

    internal sealed class CloudFlareBypassContext
    {
        public CloudFlareBypassContext(string userAgent, IList<Cookie> cookies)
        {
            this.UserAgent = userAgent;
            this.Cookies = new ReadOnlyCollection<Cookie>(cookies);
        }

        public string UserAgent { get; }
        public IEnumerable<Cookie> Cookies { get; }
    }

    internal class CloudFlareServerBypassService
    {
        readonly Uri _serverUri;
        readonly CookieContainer _cookieContainer;
        readonly ClearanceHandler _clearanceHandler;
        readonly HttpClientHandler _httpClientHandler;
        readonly HttpClient _httpClient;

        public string UserAgent { get; }

        public CloudFlareServerBypassService(Uri serverUri, string userAgent)
        {
            if (serverUri == null)
                throw new ArgumentNullException(nameof(serverUri));

            if (string.IsNullOrEmpty(userAgent))
                throw new ArgumentException("Value can not be empty.", nameof(userAgent));

            _serverUri = serverUri;
            UserAgent = userAgent;

            _cookieContainer = new CookieContainer();
            _httpClientHandler = new HttpClientHandler()
            {
                UseCookies = true,
                CookieContainer = _cookieContainer
            };

            _clearanceHandler = new ClearanceHandler(_httpClientHandler);
            _httpClient = new HttpClient(_clearanceHandler);

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }

        public async Task<CloudFlareBypassContext> GetBypassContextAsync(CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, _serverUri);
            //request.Headers.UserAgent.ParseAdd(UserAgent);

            var content = await _httpClient.SendAsync(request, cancellationToken);
            var cookies = _cookieContainer.GetCookies(_serverUri);

            var cfCookies = new List<Cookie>()
            {
                cookies.GetCFIdCookie(),
                cookies.GetCFClearanceCookie()
            };

            var result = new CloudFlareBypassContext(UserAgent, cfCookies);

            return result;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            const string userAgent = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/61.0.3163.100 Safari/537.36 OPR/48.0.2685.52";

            var bittrexUri = new Uri("https://bittrex.com");
            var bittrexFeedUri = new Uri("https://socket.bittrex.com");

            var exchange = new BittrexExchange(bittrexFeedUri);
            var cfBypassService = new CloudFlareServerBypassService(bittrexUri, userAgent);
            var cfBypassContext = cfBypassService.GetBypassContextAsync(CancellationToken.None).Result;

            var cfConfig = new CloudFlareConnectConfiguration()
            {
                Cookies = cfBypassContext.Cookies,
                UserAgent = userAgent
            };

            var config = new BittrexFeedConnectConfiguration()
            {
                AccessToken = "",
                CloudFlare = cfConfig
            };

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
