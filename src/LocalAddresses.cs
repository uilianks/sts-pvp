using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace StsPvp;

/// <summary>This PC's IPv4 addresses the other player can join, virtual networks first.</summary>
public static class LocalAddresses
{
    public enum Kind
    {
        ZeroTier,
        Hamachi,
        Lan,
    }

    public readonly record struct Address(Kind Kind, string Ip, string Interface);

    public static List<Address> Find()
    {
        var found = new List<Address>();
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return found;
        }
        foreach (var nic in interfaces)
        {
            // Virtual adapters (ZeroTier on Linux) often report Unknown instead of Up.
            if (nic.OperationalStatus is not (OperationalStatus.Up or OperationalStatus.Unknown)
                || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }
            var kind = Classify(nic);
            if (kind == null)
            {
                continue;
            }
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && !unicast.Address.ToString().StartsWith("169.254."))
                {
                    found.Add(new Address(kind.Value, unicast.Address.ToString(), nic.Name));
                }
            }
        }
        return found.OrderBy(a => a.Kind).ToList();
    }

    /// <summary>Null for adapters nobody can join through (Docker, VM bridges, ...).</summary>
    private static Kind? Classify(NetworkInterface nic)
    {
        var name = nic.Name.ToLowerInvariant();
        var description = nic.Description.ToLowerInvariant();
        if (name.StartsWith("zt") || description.Contains("zerotier") || name.Contains("zerotier"))
        {
            return Kind.ZeroTier;
        }
        if (name.StartsWith("ham") || description.Contains("hamachi") || name.Contains("hamachi"))
        {
            return Kind.Hamachi;
        }
        string[] ignored = { "docker", "br-", "veth", "virbr", "vmnet", "vboxnet", "tailscale", "wsl", "hyper-v", "vethernet" };
        if (ignored.Any(i => name.StartsWith(i) || description.Contains(i)))
        {
            return null;
        }
        return Kind.Lan;
    }
}
