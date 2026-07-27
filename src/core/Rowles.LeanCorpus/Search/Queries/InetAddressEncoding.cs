using System.Net;
using System.Net.Sockets;

namespace Rowles.LeanCorpus.Search.Queries;

internal static class InetAddressEncoding
{
    internal static byte[] Encode(IPAddress address)
        => address.AddressFamily == AddressFamily.InterNetwork
            ? address.MapToIPv6().GetAddressBytes()
            : address.GetAddressBytes();
}
