using System.Net;
using System.Diagnostics;
using System.Text;
using SharpProspero.Interop.Net;
using SharpProspero.Interop.Sysmodule;
using NativeHttp = SharpProspero.Interop.Net.Http;

namespace Waddamburo.Platform.Prospero.Native;

/// <summary>HTTP/HTTPS through PS5 services; avoids Linux socket/epoll/OpenSSL ABIs.</summary>
public sealed class ProsperoHttpMessageHandler(bool insecure = false) : HttpMessageHandler
{
    private const int MaxBody = 32 * 1024 * 1024;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new HttpRequestException("Missing request URI.");
        if (uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0)
            throw new HttpRequestException("Only HTTP/HTTPS URLs without embedded credentials are supported.");
        if (request.Content?.Headers.ContentLength > MaxBody)
            throw new HttpRequestException("Request exceeds the console HTTP body limit.");
        byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (body.Length > MaxBody) throw new HttpRequestException("Request exceeds the console HTTP body limit.");
        var headers = request.Headers.Select(pair => new KeyValuePair<string, string>(pair.Key, string.Join(", ", pair.Value))).ToList();
        if (request.Content is { } content)
            headers.AddRange(content.Headers.Where(pair => !pair.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                .Select(pair => new KeyValuePair<string, string>(pair.Key, string.Join(", ", pair.Value))));
        // Log only the origin and method: URLs, headers and bodies can carry login secrets.
        string target = $"{request.Method} {uri.GetLeftPart(UriPartial.Authority)}";
        var clock = Stopwatch.StartNew();
        Console.Error.WriteLine($"PS5 HTTP starting {target}; certificate verification {(insecure ? "disabled by config" : "enabled")}.");
        try
        {
            var response = await Task.Run(() => SendNative(request, uri.AbsoluteUri, body, headers, insecure, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            Console.Error.WriteLine($"PS5 HTTP completed {target}: {(int)response.StatusCode} after {clock.ElapsedMilliseconds} ms.");
            return response;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            if (exception is OperationCanceledException || cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine($"PS5 HTTP cancelled/timed out {target} after {clock.ElapsedMilliseconds} ms.");
                throw new OperationCanceledException("The PS5 HTTP request was cancelled or timed out.", exception, cancellationToken);
            }
            Console.Error.WriteLine($"PS5 HTTP failed {target} after {clock.ElapsedMilliseconds} ms: {exception.Message}");
            // HttpClient callers handle transport failures as HttpRequestException. In particular,
            // trust-root IO failures must not silently fault a fire-and-forget login/notice task.
            if (exception is HttpRequestException) throw;
            throw new HttpRequestException(exception.Message, exception);
        }
    }

    private static void Check(int result, string operation)
    {
        if (result < 0) throw new HttpRequestException($"PS5 {operation} failed (0x{result:x8}).");
    }

    private static unsafe HttpResponseMessage SendNative(HttpRequestMessage request, string url, byte[] body,
        List<KeyValuePair<string, string>> headers, bool insecure, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int pool = -1, ssl = -1, http = -1, template = -1, connection = -1, id = -1;
        ProsperoTrustRoots? trust = null;
        string stage = "module initialization";
        try
        {
            Sysmodule.sceSysmoduleLoadModuleInternal(0x80000009); // Net
            Sysmodule.sceSysmoduleLoadModuleInternal(0x80000018); // SSL
            Sysmodule.sceSysmoduleLoadModuleInternal(0x8000001B); // HTTP
            // A process may already have initialized Net. Pool creation verifies usable state.
            NetPool.sceNetInit();
            stage = "network pool";
            pool = NetPool.sceNetPoolCreate("waddamburo_http", 64 * 1024, 0); Check(pool, "network pool");
            ssl = Ssl.sceSslInit(1024 * 1024); Check(ssl, "TLS context");
            http = NativeHttp.sceHttpInit(pool, ssl, 256 * 1024); Check(http, "HTTP context");
            template = NativeHttp.sceHttpCreateTemplate(http, "Waddamburo-PS5/1.0", NativeHttp.Version11, 1);
            Check(template, "HTTP template");
            stage = "CA root import";
            Console.Error.WriteLine("PS5 HTTP stage: CA root import.");
            trust = new ProsperoTrustRoots();
            trust.Load(http, http: true);
            stage = "TLS/request policy";
            const uint verification = NativeHttp.SslFlagServerVerify | NativeHttp.SslFlagCnCheck
                | NativeHttp.SslFlagNotAfterCheck | NativeHttp.SslFlagNotBeforeCheck | NativeHttp.SslFlagKnownCaCheck;
            Check(insecure ? NativeHttp.sceHttpsDisableOption(template, verification)
                : NativeHttp.sceHttpsEnableOption(template, verification), "TLS verification policy");
            // The SDK's ordinal TLS selector is not a verified firmware protocol value.
            // Keep firmware negotiation defaults; CA/name/time verification stays enabled.
            Console.Error.WriteLine("PS5 HTTP TLS protocol: firmware defaults (no SDK version override).");
            Check(NativeHttp.sceHttpSetAutoRedirect(template, 0), "redirect policy");
            Check(NativeHttp.sceHttpSetConnectTimeOut(template, 15_000_000), "connect timeout");
            Check(NativeHttp.sceHttpSetResolveTimeOut(template, 15_000_000), "DNS timeout");
            Check(NativeHttp.sceHttpSetRecvTimeOut(template, 15_000_000), "receive timeout");
            Check(NativeHttp.sceHttpSetSendTimeOut(template, 15_000_000), "send timeout");
            stage = "connection/request creation";
            connection = NativeHttp.sceHttpCreateConnectionWithURL(template, url, 0); Check(connection, "connection");
            id = NativeHttp.sceHttpCreateRequestWithURL2(connection, request.Method.Method, url, (ulong)body.Length);
            Check(id, "request");
            foreach (var header in headers)
                Check(NativeHttp.sceHttpAddRequestHeader(id, header.Key, header.Value, NativeHttp.HeaderOverwrite), "request header");
            int requestId = id;
            using var cancel = token.Register(() => NativeHttp.sceHttpAbortRequest(requestId));
            token.ThrowIfCancellationRequested();
            stage = "DNS/TCP/TLS/send";
            Console.Error.WriteLine("PS5 HTTP stage: DNS/TCP/TLS/send.");
            int sent;
            fixed (byte* data = body)
                sent = NativeHttp.sceHttpSendRequest(id, body.Length == 0 ? null : data, (nuint)body.Length);
            if (sent < 0 && !token.IsCancellationRequested)
            {
                int nativeErrno = 0, sslError = 0; uint detail = 0;
                int errnoResult = NativeHttp.sceHttpGetLastErrno(id, &nativeErrno);
                int sslResult = NativeHttp.sceHttpsGetSslError(id, &sslError, &detail);
                throw new HttpRequestException($"PS5 send/TLS handshake failed (0x{sent:x8}); "
                    + $"errno=0x{nativeErrno:x8} (query 0x{errnoResult:x8}), "
                    + $"TLS=0x{sslError:x8}, verify=0x{detail:x8} (query 0x{sslResult:x8}).");
            }
            Check(sent, "send/TLS handshake");
            token.ThrowIfCancellationRequested();
            stage = "response status";
            int status = 0;
            Check(NativeHttp.sceHttpGetStatusCode(id, &status), "status");
            var response = new HttpResponseMessage((HttpStatusCode)status) { RequestMessage = request };
            try
            {
                stage = "response body";
                Console.Error.WriteLine($"PS5 HTTP stage: response body (status {status}).");
                using var output = new MemoryStream();
                var buffer = new byte[32 * 1024];
                if (request.Method != System.Net.Http.HttpMethod.Head)
                    fixed (byte* data = buffer)
                        while (true)
                        {
                            token.ThrowIfCancellationRequested();
                            int read = NativeHttp.sceHttpReadData(id, data, (nuint)buffer.Length); Check(read, "response read");
                            if (read == 0) break;
                            if (output.Length + read > MaxBody) throw new HttpRequestException("Response exceeds the console HTTP body limit.");
                            output.Write(buffer, 0, read);
                        }
                response.Content = new ByteArrayContent(output.ToArray());
                byte* raw = null; nuint size = 0;
                Check(NativeHttp.sceHttpGetAllResponseHeaders(id, &raw, &size), "response headers");
                if (raw != null && size > 0)
                    foreach (var line in Encoding.UTF8.GetString(raw, checked((int)size)).TrimEnd('\0').Split('\n'))
                    {
                        int colon = line.IndexOf(':');
                        if (colon <= 0) continue;
                        string name = line[..colon].Trim(), value = line[(colon + 1)..].Trim();
                        if (!response.Headers.TryAddWithoutValidation(name, value))
                            response.Content.Headers.TryAddWithoutValidation(name, value);
                    }
                return response;
            }
            catch { response.Dispose(); throw; }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            throw new HttpRequestException($"{stage}: {exception.Message}", exception);
        }
        finally
        {
            if (id >= 0) NativeHttp.sceHttpDeleteRequest(id);
            if (connection >= 0) NativeHttp.sceHttpDeleteConnection(connection);
            if (template >= 0) NativeHttp.sceHttpDeleteTemplate(template);
            if (http >= 0) NativeHttp.sceHttpTerm(http);
            if (ssl >= 0) Ssl.sceSslTerm(ssl);
            trust?.Dispose();
            if (pool >= 0) NetPool.sceNetPoolDestroy(pool);
        }
    }
}
