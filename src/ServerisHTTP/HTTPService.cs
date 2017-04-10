using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

/*
HTTP/1.1 Message Format:
https://tools.ietf.org/html/rfc7230#section-3

HTTP Documentation:
http://httpwg.org/specs/
*/

namespace ServerisHTTP
{
    /// <summary>
    /// My own implementation of a HTTP service. As of now, it only responds to simple GET requests.
    /// </summary>
    internal static class HTTPService
    {
        const string
            symbolSP = " ",
            symbolCRLF = "\r\n";

        /// <summary>
        /// Sends a response to an HTTP request.
        /// </summary>
        /// <param name="socket">Connection.</param>
        /// <param name="requestBytes">The array must be truncated to match size of request.</param>
        /// <exception cref="Exception">Can throw ...</exception>
        public static void Respond(Socket socket, byte[] requestBytes)
        {
            const string
                methodTokenGET = "GET";

            var receivedStr = Encoding.ASCII.GetString(requestBytes);
            if (receivedStr.StartsWith(methodTokenGET + symbolSP, StringComparison.Ordinal))
            {
                int lineEnd = receivedStr.IndexOf(symbolCRLF);
                if (lineEnd < 0) lineEnd = receivedStr.Length;
                int firstSP = receivedStr.IndexOf(symbolSP); // We already know there is an SP symbol at the start.
                int startIdx = firstSP + 1;
                int lastSP = receivedStr.LastIndexOf(symbolSP, lineEnd - 1, lineEnd - 1 - startIdx);
                if (startIdx < lastSP)
                {
                    string encodedTarget = receivedStr.Substring(startIdx, lastSP - startIdx);
                    if (encodedTarget.Contains(symbolSP))
                    {
                        // Should return some error here.
                        SendHTTP(socket, ResponseTypes.BadRequest);
                        return;
                    }
                    else
                    {
                        string decodedTarget = Uri.UnescapeDataString(encodedTarget);
                        RespondToGET(socket, receivedStr, decodedTarget);
                        return;
                    }
                }
            }
            SendHTTP(socket, ResponseTypes.NotImplemented);
        }

        /// <summary>
        /// Sends a response to a GET request.
        /// </summary>
        /// <param name="socket">Connection socket.</param>
        /// <param name="receivedStr">Client's request message.</param>
        /// <param name="requestTarget">Must be in unescaped (decoded) form.</param>
        /// <remarks>
        /// HTTP/1.1 GET:
        /// <para/>https://tools.ietf.org/html/rfc7231#section-4.3.1
        /// </remarks>
        private static void RespondToGET(Socket socket, string receivedStr, string requestTarget)
        {
            const string defaultRequestStr = "/";
            if (String.IsNullOrEmpty(requestTarget) || requestTarget == defaultRequestStr)
            {
                if (Settings.RedirectToIndex)
                {
                    SendDefaultRedirect(socket, receivedStr);
                    return;
                }
                else
                {
                    requestTarget = Settings.HTTPDefaultPage;
                }
            }

            string trimmedRequest = requestTarget.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fi = new FileInfo(Path.Combine(Settings.HTTPRootDirectory, trimmedRequest));
            if (fi.FullName.StartsWith(Settings.HTTPRootDirectory) && fi.Exists)
            {
                var pageData = File.ReadAllBytes(fi.FullName);
                SendHTTP(socket, pageData);
                return;
            }

            SendHTTP(socket, ResponseTypes.NotFound);
        }

        /// <summary>
        /// Sends a web-page that immediately redirects to <see cref="Settings.HTTPDefaultPage"/>.
        /// </summary>
        /// <param name="socket">Connection.</param>
        /// <param name="receivedStr">Client's request message.</param>
        /// <remarks>
        /// HTTP/1.1 Host:
        /// https://tools.ietf.org/html/rfc7230#section-5.4
        /// </remarks>
        private static void SendDefaultRedirect(Socket socket, string receivedStr)
        {
            const string redirectResponse =
@"<html>
<head>
<meta http-equiv=""Content-Type"" content=""text/html; charset=UTF-8"">
<title>{1}</title>
<meta http-equiv=""REFRESH"" content=""0;url={0}"">
</head>
Hello. <A href= ""{0}"" > Redirecting...</ A >
</ body >
</ html >";

            string rootPath = null;
            const string fieldStrHost = "Host: ";
            int hostIdx = receivedStr.IndexOf(fieldStrHost);
            if (hostIdx >= 0)
            {
                int startIdx = hostIdx + fieldStrHost.Length;
                int endIdx = receivedStr.IndexOf(symbolCRLF, hostIdx);
                if (endIdx >= 0) rootPath = receivedStr.Substring(startIdx, endIdx - startIdx);
            }

            Uri targetUri = null;
            if (Uri.TryCreate(Settings.HTTPDefaultPage, UriKind.Relative, out targetUri))
            {
                Uri rootUri;
                if (Uri.TryCreate(Uri.UriSchemeHttp + Uri.SchemeDelimiter + rootPath, UriKind.Absolute, out rootUri))
                {
                    targetUri = new Uri(rootUri, targetUri);
                }
            }
            string dataString = String.Format(redirectResponse,
                WebUtility.HtmlEncode(targetUri?.ToString()),
                WebUtility.HtmlEncode(rootPath));
            byte[] pageData = Encoding.UTF8.GetBytes(dataString);
            SendHTTP(socket, ResponseTypes.OK, pageData);
        }

        private static void SendHTTP(Socket socket, byte[] pageData)
        {
            SendHTTP(socket, ResponseTypes.OK, pageData);
        }

        /// <summary>
        /// Creates an HTTP header based on <paramref name="responseType"/> and
        /// appends <paramref name="dataBytes"/> if specified.
        /// </summary>
        private static void SendHTTP(Socket socket, ResponseTypes responseType, byte[] dataBytes = null)
        {
            const string headerTemplate =
                @"HTTP/1.1 {0} {1}"
                + symbolCRLF + "Content-Type: text/html";
            string header = String.Format(headerTemplate, (int)responseType, responseType.GetMessage());
            byte[] bytes;
            if (dataBytes == null)
            {
                bytes = Encoding.ASCII.GetBytes(header);
            }
            else
            {
                header += symbolCRLF + symbolCRLF;
                // Data bytes may be encoded differently from the header.
                bytes = Encoding.ASCII.GetBytes(header).Concat(dataBytes).ToArray();
            }
            socket.Send(bytes);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// HTTP/1.1 Status Code and Reason Phrase:
        /// https://www.w3.org/Protocols/rfc2616/rfc2616-sec6.html#sec6.1.1
        /// </remarks>
        private enum ResponseTypes
        {
            OK = 200,
            BadRequest = 400,
            NotFound = 404,
            NotImplemented = 501
        }

        private static string GetMessage(this ResponseTypes type)
        {
            switch (type)
            {
                case ResponseTypes.OK: return "OK";
                case ResponseTypes.BadRequest: return "Bad request";
                case ResponseTypes.NotFound: return "Not found";
                case ResponseTypes.NotImplemented: return "Not implemented";
                default: return type.ToString();
            }
        }
    }
}
