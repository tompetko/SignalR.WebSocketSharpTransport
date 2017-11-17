using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNet.SignalR.Client.Http;
using NineDigit.SignalR.WebSocketSharpTransport.Extensions;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using WebSocketSharp;

namespace NineDigit.SignalR.WebSocketSharpTransport
{
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
                //this._headers.Add(
                //    new KeyValuePair<string, string>(headerEntry.Key, headerEntry.Value));
                this.SetHeader(headerEntry.Key, headerEntry.Value);
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

        private void SetHeader(string key, string value)
        {
            for (int i = _headers.Count - 1; i >= 0; i--)
            {
                var header = _headers[i];

                if (header.Key == key)
                    _headers.RemoveAt(i);
            }

            _headers.Add(new KeyValuePair<string, string>(key, value));
        }
    }
}
