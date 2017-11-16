using CloudFlareUtilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using System.Net;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNet.SignalR.Client.Http;
using Microsoft.AspNet.SignalR.Client.Transports;

namespace NineDigit.BittrexTest
{
    internal class BittrexExchange
    {
        public HubConnection Connection { get; private set; }
        public IHubProxy HubProxy { get; private set; }

        public BittrexExchange()
        {
            Connection = new HubConnection("https://socket.bittrex.com");
        }

        public async Task setupWebsockets(string __cfduid, string cf_clearance, string accessToken)
        {
            Connection.TraceLevel = TraceLevels.Events;
            Connection.TraceWriter = Console.Out;

            if (!__cfduid.Equals(string.Empty) || !cf_clearance.Equals(string.Empty))
            {
                Connection.CookieContainer = new CookieContainer();
                var target = new Uri("https://socket.bittrex.com");

                Cookie __cfduidCookie = new Cookie("__cfduid", __cfduid);
                Cookie cf_clearanceCookie = new Cookie("cf_clearance", cf_clearance);

                if (!string.IsNullOrEmpty(accessToken))
                {
                    var appCookieKey = ".AspNet.ApplicationCookie";
                    var aspNetApplicationCookie = new Cookie(appCookieKey, accessToken, "/", ".bittrex.com");

                    Connection.CookieContainer.Add(aspNetApplicationCookie);
                }
                
                Connection.CookieContainer.Add(target, __cfduidCookie);
                Connection.CookieContainer.Add(target, cf_clearanceCookie);

                Connection.Closed += Connection_Closed;
                Connection.ConnectionSlow += Connection_ConnectionSlow;
                Connection.Error += Connection_Error;
                Connection.Received += Connection_Received;
                Connection.Reconnected += Connection_Reconnected;
                Connection.Reconnecting += Connection_Reconnecting;
                Connection.StateChanged += Connection_StateChanged;

                HubProxy = Connection.CreateHubProxy("CoreHub");

                HubProxy.On<dynamic>("updateSummaryState", this.OnUpdateSummaryState);
                HubProxy.On<dynamic>("updateExchangeState", this.OnUpdateExchangeState);
                HubProxy.On<dynamic>("updateOrderState", this.OnUpdateOrderState);

                try
                {
                    var httpClient = new DefaultHttpClient();
                    var transports = new IClientTransport[]
                    {
                        new WebSocketTransportEx(httpClient),
                        new LongPollingTransport(httpClient)
                    };

                    var autoTransport = new AutoTransport(
                        httpClient, transports);

                    this.Connection.TransportConnectTimeout = new TimeSpan(0, 0, 15);

                    await Connection.Start(autoTransport);
                    //await Connection.Start();
                }
                catch (HttpRequestException)
                {
                    return;
                }
            }
            else
            {
                Console.WriteLine("Got no clearance from Cloudaflare", "cloudflare.log");
            }

            //signal.WaitOne();
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
        const string CFIdCookieName = "__cfduid";
        const string CFClearanceCookieName = "cf_clearance";

        public static string GetCFIdCookieValue(this CookieCollection self)
        {
            return self[CFIdCookieName]?.Value;
        }

        public static string GetCFClearanceCookieValue(this CookieCollection self)
        {
            return self[CFClearanceCookieName]?.Value;
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

    internal class ClearanceHandlerEx : ClearanceHandler
    {
        private const string IdCookieName = "__cfduid";
        private const string ClearanceCookieName = "cf_clearance";

        public string CfDUID { get; private set; }
        public string CfClearance { get; private set; }

        protected HttpClientHandler ClientHandler => InnerHandler.GetMostInnerHandler() as HttpClientHandler;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd("SignalR.Client.NET45/2.2.2.0 (Microsoft Windows NT 6.2.9200.0)");
            //request.Headers.UserAgent.ParseAdd("websocket-sharp/1.0");

            var result = await base.SendAsync(request, cancellationToken);
            
            this.CfDUID = this.ClientHandler.CookieContainer.GetCookiesByName(request.RequestUri, IdCookieName).FirstOrDefault()?.Value;
            this.CfClearance = this.ClientHandler.CookieContainer.GetCookiesByName(request.RequestUri, ClearanceCookieName).FirstOrDefault()?.Value;

            return result;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            const string userAgent = "SignalR.Client.NET45/2.2.2.0 (Microsoft Windows NT 6.2.9200.0)";
            //const string userAgent = "websocket-sharp/1.0";

            var bittrexUri = new Uri("https://bittrex.com");
            var bittrexFeedUri = new Uri("https://socket.bittrex.com");

            var cookieContainer = new CookieContainer();
            var handler = new ClearanceHandler(new HttpClientHandler()
            {
                UseCookies = true,
                CookieContainer = cookieContainer
            });

            var request = new HttpRequestMessage(HttpMethod.Get, bittrexUri);

            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd(userAgent);

            var client = new HttpClient(handler);

            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

            var content = client.SendAsync(request).Result;

            var exchange = new BittrexExchange();

            //exchange.GetMarketSummaries(); //Rest API call

            var cookies = cookieContainer.GetCookies(bittrexUri);
            var cfduid = cookies.GetCFIdCookieValue();
            var cfclearance = cookies.GetCFClearanceCookieValue();

            var accessToken = "";
            
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

            exchange.setupWebsockets(cfduid, cfclearance, accessToken).Wait();
                
            Console.ReadLine();
        }
    }

    //Notice SignalR version I am using
    //<package id="Microsoft.AspNet.SignalR.Client" version="2.2.2" targetFramework="net452" />

    //The only reason this implementation fail is that CloudFlare expect same header,
    // therefore, change as following inside CloudFlareUtilities ClearanceHandler.cs
    /*private static void EnsureClientHeader(HttpRequestMessage request)
    {
        //if (!request.Headers.UserAgent.Any())
        //    request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Client", "1.0"));
        if (!request.Headers.UserAgent.Any())
            request.Headers.UserAgent.ParseAdd("SignalR.Client.NET45/2.2.2.0 (Microsoft Windows NT 6.2.9200.0)");
    }*/
}
