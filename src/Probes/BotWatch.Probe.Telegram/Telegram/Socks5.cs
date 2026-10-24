using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BotWatch.Probe.Telegram.Telegram;

// подключение к Telegram через SOCKS5 (RFC 1928, логин/пароль по RFC 1929).
// отдельная библиотека ради сотни байт рукопожатия не нужна
internal static class Socks5
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static async Task<TcpClient> ConnectAsync(Uri proxy, string host, int port)
    {
        var tcp = new TcpClient();
        try
        {
            using var timeout = new CancellationTokenSource(Timeout);
            await tcp.ConnectAsync(proxy.Host, proxy.Port, timeout.Token);
            var stream = tcp.GetStream();

            await AuthenticateAsync(stream, proxy, timeout.Token);
            await SendConnectAsync(stream, host, port, timeout.Token);

            return tcp;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    private static async Task AuthenticateAsync(NetworkStream stream, Uri proxy, CancellationToken cancellationToken)
    {
        var credentials = Uri.UnescapeDataString(proxy.UserInfo).Split(':', 2);
        var withPassword = credentials[0].Length > 0;

        // предлагаем «без авторизации» и, если есть логин, «логин/пароль»
        byte[] greeting = withPassword ? [5, 2, 0, 2] : [5, 1, 0];
        await stream.WriteAsync(greeting, cancellationToken);

        var reply = await ReadAsync(stream, 2, cancellationToken);
        if (reply[0] != 5)
        {
            throw new IOException("Proxy is not a SOCKS5 proxy");
        }

        if (reply[1] == 0)
        {
            return;
        }

        if (reply[1] != 2 || !withPassword)
        {
            throw new IOException("SOCKS5 proxy requires an authentication method we can't provide");
        }

        var user = Encoding.UTF8.GetBytes(credentials[0]);
        var password = Encoding.UTF8.GetBytes(credentials.Length > 1 ? credentials[1] : "");
        await stream.WriteAsync((byte[])[1, (byte)user.Length, .. user, (byte)password.Length, .. password], cancellationToken);

        var status = await ReadAsync(stream, 2, cancellationToken);
        if (status[1] != 0)
        {
            throw new IOException("SOCKS5 proxy rejected the login or password");
        }
    }

    private static async Task SendConnectAsync(NetworkStream stream, string host, int port, CancellationToken cancellationToken)
    {
        byte[] address = IPAddress.TryParse(host, out var ip)
            ? [ip.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)4 : (byte)1, .. ip.GetAddressBytes()]
            : [3, (byte)host.Length, .. Encoding.ASCII.GetBytes(host)];

        await stream.WriteAsync((byte[])[5, 1, 0, .. address, (byte)(port >> 8), (byte)port], cancellationToken);

        var header = await ReadAsync(stream, 4, cancellationToken);
        if (header[1] != 0)
        {
            throw new IOException($"SOCKS5 proxy could not connect to {host}:{port}, code {header[1]}");
        }

        // дальше прокси сообщает свой адрес, он нам не нужен, но его надо вычитать из потока
        var boundLength = header[3] switch
        {
            1 => 4,
            4 => 16,
            3 => (await ReadAsync(stream, 1, cancellationToken))[0],
            _ => throw new IOException($"SOCKS5 proxy returned unknown address type {header[3]}")
        };

        await ReadAsync(stream, boundLength + 2, cancellationToken);
    }

    private static async Task<byte[]> ReadAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        return buffer;
    }
}
