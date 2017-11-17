using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNet.SignalR.Client.Http;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SignalR.WebSocketSharpTransport
{
    public class DefaultHttpClientEx : IHttpClient
    {
        private readonly IHttpClient _httpClient;
        private IConnection _connection;

        public DefaultHttpClientEx()
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
}
