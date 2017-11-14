using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNet.SignalR.Client.Http;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace NineDigit.BittrexTest
{
    internal class WebSocketWrapperRequestEx : IRequest
    {
        private readonly ClientWebSocket2 _clientWebSocket;
        private IConnection _connection;

        public WebSocketWrapperRequestEx(ClientWebSocket2 clientWebSocket, IConnection connection)
        {
            _clientWebSocket = clientWebSocket;
            _connection = connection;
            PrepareRequest();
        }

        public string UserAgent
        {
            get
            {
                return this._clientWebSocket.Options.RequestHeaders.FirstOrDefault(i => i.Key == "User-Agent").Value;
            }
            set
            {
                this._clientWebSocket.Options.RequestHeaders["User-Agent"] = value;
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:No upstream or protected callers", Justification = "Keeping the get accessors for future use")]
        public ICredentials Credentials
        {
            get
            {
                return _clientWebSocket.Options.Credentials;
            }
            set
            {
                _clientWebSocket.Options.Credentials = value;
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:No upstream or protected callers", Justification = "Keeping the get accessors for future use")]
        public CookieContainer CookieContainer
        {
            get
            {
                return _clientWebSocket.Options.Cookies;
            }
            set
            {
                _clientWebSocket.Options.Cookies = value;
            }
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:No upstream or protected callers", Justification = "Keeping the get accessors for future use")]
        public IWebProxy Proxy
        {
            get
            {
                return _clientWebSocket.Options.Proxy;
            }
            set
            {
                _clientWebSocket.Options.Proxy = value;
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
                throw new ArgumentNullException("headers");
            }

            foreach (KeyValuePair<string, string> headerEntry in headers)
            {
                _clientWebSocket.Options.RequestHeaders[headerEntry.Key] = headerEntry.Value;
            }
        }

        public void AddClientCerts(X509CertificateCollection certificates)
        {
            if (certificates == null)
            {
                throw new ArgumentNullException("certificates");
            }

            _clientWebSocket.Options.InternalClientCertificates = certificates;
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
                CookieContainer = _connection.CookieContainer;
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
    }
}
